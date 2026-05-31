using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RelayFlow.Configuration;
using RelayFlow.Diagnostics;
using RelayFlow.Resilience;
using RelayFlow.Security;
using RelayFlow.Transforms;
using Yarp.ReverseProxy.Forwarder;

namespace RelayFlow.Forwarding
{
    /// <summary>
    /// The core relay engine. Resolves the upstream destination, enforces the SSRF allow-list and
    /// the per-destination circuit breaker, builds the per-route transformer (credential swap +
    /// header hygiene), and delegates the actual byte copying / streaming / hop-by-hop handling to
    /// YARP's <see cref="IHttpForwarder"/>. Emits metrics and a tracing span per request.
    /// </summary>
    public sealed class RelayForwarder
    {
        private readonly IHttpForwarder _forwarder;
        private readonly HttpMessageInvoker _upstreamClient;
        private readonly RelayOptions _options;
        private readonly CircuitBreakerRegistry _breakers;
        private readonly RelayMetrics _metrics;
        private readonly IServiceProvider _services;
        private readonly ILogger<RelayForwarder> _logger;

        /// <summary>Creates the forwarder. Resolved from DI.</summary>
        public RelayForwarder(
            IHttpForwarder forwarder,
            UpstreamHttpClient upstreamClient,
            RelayOptions options,
            CircuitBreakerRegistry breakers,
            RelayMetrics metrics,
            IServiceProvider services,
            ILogger<RelayForwarder> logger)
        {
            _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
            _upstreamClient = (upstreamClient ?? throw new ArgumentNullException(nameof(upstreamClient))).Invoker;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _breakers = breakers ?? throw new ArgumentNullException(nameof(breakers));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Relays the current request to the route's resolved destination. Writes the response
        /// (or an error status) directly to <paramref name="context"/>.
        /// </summary>
        public async Task RelayAsync(HttpContext context, RelayRoute route)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (route == null) throw new ArgumentNullException(nameof(route));

            // 1. Resolve destination from the template + route values.
            var destination = DestinationResolver.Resolve(context, route.DestinationTemplate);
            if (!destination.Success)
            {
                _logger.LogWarning("RelayFlow: destination resolution failed ({Error}) for {Path}.",
                    destination.Error, context.Request.Path);
                _metrics.RecordRejected("destination_resolution_failed");
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            string authority = destination.Uri!.GetLeftPart(UriPartial.Authority);

            // Start a tracing span for the relay (no-op unless a listener is attached).
            using var activity = RelayActivitySource.Source.StartActivity("relayflow.forward", ActivityKind.Client);
            activity?.SetTag("relayflow.destination", authority);
            activity?.SetTag("http.request.method", context.Request.Method);

            // 2. SSRF allow-list enforcement. Deny-by-default.
            var decision = _options.Destinations.Evaluate(destination.Uri!);
            if (!decision.IsAllowed)
            {
                _logger.LogWarning("RelayFlow: destination {Destination} denied ({Reason}).",
                    destination.Uri, decision.Reason);
                _metrics.RecordRejected("ssrf_denied");
                activity?.SetStatus(ActivityStatusCode.Error, "ssrf_denied");
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            // 3. Circuit breaker: fail fast if the destination is currently tripped.
            CircuitBreaker? breaker = _breakers.Enabled ? _breakers.GetBreaker(destination.Uri!) : null;
            if (breaker != null && !breaker.TryAcquire())
            {
                _logger.LogWarning("RelayFlow: circuit open for {Destination}; rejecting.", authority);
                _metrics.RecordRejected("circuit_open");
                activity?.SetStatus(ActivityStatusCode.Error, "circuit_open");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            // 4. Build the per-route transformer (credential provider + trusted header names).
            var credentialProvider = ResolveCredentialProvider(route, out var trustedHeaders);
            var transformer = new RelayTransformer(route, _options, credentialProvider, trustedHeaders, destination.Uri!);

            var requestConfig = new ForwarderRequestConfig
            {
                ActivityTimeout = route.Timeout ?? _options.DefaultTimeout,
            };

            string prefix = authority;
            var stopwatch = ValueStopwatch.StartNew();

            // 5. Forward. YARP handles streaming, hop-by-hop, cancellation, and error mapping.
            var error = await _forwarder.SendAsync(
                context, prefix, _upstreamClient, requestConfig, transformer)
                .ConfigureAwait(false);

            double elapsedMs = stopwatch.GetElapsedMilliseconds();
            int statusCode = context.Response.StatusCode;

            if (error != ForwarderError.None)
            {
                var feature = context.GetForwarderErrorFeature();
                _logger.LogWarning(feature?.Exception,
                    "RelayFlow: forwarding error {Error} for {Path}.", error, context.Request.Path);
                // YARP already set an appropriate status code (502/504/etc.). Do not leak details.
                breaker?.RecordFailure();
                activity?.SetStatus(ActivityStatusCode.Error, error.ToString());
            }
            else
            {
                // Treat upstream 5xx as a failure for breaking purposes; 4xx is the caller's fault.
                if (statusCode >= 500) breaker?.RecordFailure();
                else breaker?.RecordSuccess();
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            activity?.SetTag("http.response.status_code", statusCode);
            _metrics.RecordForwarded(authority, statusCode, elapsedMs);
        }

        private ICredentialProvider ResolveCredentialProvider(RelayRoute route, out string[] trustedHeaders)
        {
            // An explicit per-route provider wins.
            if (route.CredentialProvider != null)
            {
                trustedHeaders = route.CredentialProvider is ClaimsForwardingCredentialProvider cfp
                    ? ToArray(cfp)
                    : Array.Empty<string>();
                return route.CredentialProvider;
            }

            switch (route.CredentialMode)
            {
                case CredentialMode.None:
                case CredentialMode.PassThrough:
                    trustedHeaders = Array.Empty<string>();
                    return NoopCredentialProvider.Instance;

                case CredentialMode.ForwardClaims:
                    var claimsProvider = new ClaimsForwardingCredentialProvider(route.ClaimMappings);
                    trustedHeaders = ToArray(claimsProvider);
                    return claimsProvider;

                case CredentialMode.ServiceToken:
                case CredentialMode.ApiKey:
                    // Resolve a registered provider from DI (must be supplied by the app).
                    var provider = _services.GetService<ICredentialProvider>()
                        ?? throw new InvalidOperationException(
                            $"CredentialMode.{route.CredentialMode} requires an ICredentialProvider " +
                            "to be registered. Call services.AddSingleton<ICredentialProvider>(...).");
                    trustedHeaders = Array.Empty<string>();
                    return provider;

                default:
                    trustedHeaders = Array.Empty<string>();
                    return NoopCredentialProvider.Instance;
            }
        }

        private static string[] ToArray(ClaimsForwardingCredentialProvider provider)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var h in provider.HeaderNames) list.Add(h);
            return list.ToArray();
        }
    }

    /// <summary>
    /// A lightweight, allocation-free elapsed-time measurement based on
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.
    /// </summary>
    internal readonly struct ValueStopwatch
    {
        private static readonly double TimestampToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private readonly long _start;

        private ValueStopwatch(long start) => _start = start;

        public static ValueStopwatch StartNew() => new ValueStopwatch(System.Diagnostics.Stopwatch.GetTimestamp());

        public double GetElapsedMilliseconds() =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - _start) * TimestampToMs;
    }
}
