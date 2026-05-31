using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RelayFlow.Configuration;
using RelayFlow.Security;
using RelayFlow.Transforms;
using Yarp.ReverseProxy.Forwarder;

namespace RelayFlow.Forwarding
{
    /// <summary>
    /// The core relay engine. Resolves the upstream destination, enforces the SSRF allow-list,
    /// builds the per-route transformer (credential swap + header hygiene), and delegates the
    /// actual byte copying / streaming / hop-by-hop handling to YARP's
    /// <see cref="IHttpForwarder"/>.
    /// </summary>
    public sealed class RelayForwarder
    {
        private readonly IHttpForwarder _forwarder;
        private readonly HttpMessageInvoker _upstreamClient;
        private readonly RelayOptions _options;
        private readonly IServiceProvider _services;
        private readonly ILogger<RelayForwarder> _logger;

        /// <summary>Creates the forwarder. Resolved from DI.</summary>
        public RelayForwarder(
            IHttpForwarder forwarder,
            UpstreamHttpClient upstreamClient,
            RelayOptions options,
            IServiceProvider services,
            ILogger<RelayForwarder> logger)
        {
            _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
            _upstreamClient = (upstreamClient ?? throw new ArgumentNullException(nameof(upstreamClient))).Invoker;
            _options = options ?? throw new ArgumentNullException(nameof(options));
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
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            // 2. SSRF allow-list enforcement. Deny-by-default.
            var decision = _options.Destinations.Evaluate(destination.Uri!);
            if (!decision.IsAllowed)
            {
                _logger.LogWarning("RelayFlow: destination {Destination} denied ({Reason}).",
                    destination.Uri, decision.Reason);
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            // 3. Build the per-route transformer (credential provider + trusted header names).
            var credentialProvider = ResolveCredentialProvider(route, out var trustedHeaders);
            var transformer = new RelayTransformer(route, _options, credentialProvider, trustedHeaders, destination.Uri!);

            var requestConfig = new ForwarderRequestConfig
            {
                ActivityTimeout = route.Timeout ?? _options.DefaultTimeout,
            };

            // 4. Forward. YARP handles streaming, hop-by-hop, cancellation, and error mapping.
            // The destination prefix is the scheme://host:port; the transformer's base sets the
            // full request URI from the resolved absolute destination path+query.
            string prefix = GetPrefix(destination.Uri!);

            var error = await _forwarder.SendAsync(
                context, prefix, _upstreamClient, requestConfig, transformer)
                .ConfigureAwait(false);

            if (error != ForwarderError.None)
            {
                var feature = context.GetForwarderErrorFeature();
                _logger.LogWarning(feature?.Exception,
                    "RelayFlow: forwarding error {Error} for {Path}.", error, context.Request.Path);
                // YARP already set an appropriate status code (502/504/etc.). Do not leak details.
            }
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

        private static string GetPrefix(Uri destination)
        {
            // Scheme://authority (no path). The transformer base will set the full URI using
            // the destination's PathAndQuery, so we pass the full absolute URI's left part.
            return destination.GetLeftPart(UriPartial.Authority);
        }
    }
}
