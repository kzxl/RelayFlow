using System;
using RelayFlow.Resilience;
using RelayFlow.Security;

namespace RelayFlow.Configuration
{
    /// <summary>
    /// Global RelayFlow options configured via <c>AddRelayFlow</c>. Per-route settings on
    /// <see cref="RelayRoute"/> take precedence where applicable.
    /// </summary>
    public sealed class RelayOptions
    {
        /// <summary>
        /// The SSRF allow-list policy. Deny-by-default: you must register at least one allowed
        /// origin or every relay will be rejected. This is intentional.
        /// </summary>
        public DestinationPolicy Destinations { get; } = new DestinationPolicy();

        /// <summary>
        /// Per-destination circuit breaker settings. When tripped, the relay fails fast with
        /// <c>503 Service Unavailable</c> instead of piling requests onto a failing internal API.
        /// </summary>
        public CircuitBreakerOptions CircuitBreaker { get; } = new CircuitBreakerOptions();

        /// <summary>
        /// Default upstream request timeout when a route does not specify one. Defaults to 100s
        /// (matching <see cref="System.Net.Http.HttpClient"/>'s historical default).
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);

        /// <summary>
        /// When true (default), known sensitive response headers (Server, X-Powered-By, etc.)
        /// are stripped from responses returned to the public caller.
        /// </summary>
        public bool StripSensitiveResponseHeaders { get; set; } = true;

        /// <summary>
        /// When true (default), the inbound <c>Authorization</c> header is removed before
        /// forwarding unless the route uses <see cref="CredentialMode.PassThrough"/>. This
        /// prevents the public token from leaking to the internal API by accident.
        /// </summary>
        public bool StripInboundAuthorization { get; set; } = true;

        /// <summary>
        /// Connection pool lifetime for the shared upstream <see cref="System.Net.Http.SocketsHttpHandler"/>.
        /// Recycling connections lets DNS changes take effect. Defaults to 2 minutes.
        /// </summary>
        public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// When true, automatic redirect following on the upstream client is disabled (default).
        /// A relay should return upstream redirects to the caller rather than chase them.
        /// </summary>
        public bool DisableUpstreamRedirects { get; set; } = true;
    }
}
