using System;
using System.Collections.Generic;
using RelayFlow.Security;

namespace RelayFlow.Configuration
{
    /// <summary>
    /// The resolved configuration for a single relay route, produced by <c>MapRelay</c> and the
    /// builder fluent calls, then consumed by the forwarding pipeline.
    /// </summary>
    public sealed class RelayRoute
    {
        /// <summary>Creates a route from the public path pattern to the destination template.</summary>
        public RelayRoute(string pattern, string destinationTemplate)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Route pattern is required.", nameof(pattern));
            if (string.IsNullOrWhiteSpace(destinationTemplate))
                throw new ArgumentException("Destination template is required.", nameof(destinationTemplate));

            Pattern = pattern;
            DestinationTemplate = destinationTemplate;
        }

        /// <summary>The public endpoint route pattern, e.g. <c>/api/orders/{**rest}</c>.</summary>
        public string Pattern { get; }

        /// <summary>
        /// The upstream destination template, e.g. <c>https://internal:5000/orders/{**rest}</c>.
        /// Route values captured by <see cref="Pattern"/> are substituted into the template.
        /// </summary>
        public string DestinationTemplate { get; }

        /// <summary>How credentials are handled when forwarding. Defaults to the safe <see cref="CredentialMode.None"/>.</summary>
        public CredentialMode CredentialMode { get; set; } = CredentialMode.None;

        /// <summary>
        /// When set, the response header allow-list: only these (plus framing essentials) are
        /// returned to the caller. When null, all non-sensitive response headers pass through.
        /// </summary>
        public HashSet<string>? ResponseHeaderAllowList { get; set; }

        /// <summary>
        /// Additional request header names to strip before forwarding (on top of hop-by-hop and
        /// any spoofable trusted headers). Case-insensitive.
        /// </summary>
        public HashSet<string> StripRequestHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-route timeout for the upstream call. Null uses the global default.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// An optional explicit credential provider for this route. When null, the provider is
        /// resolved based on <see cref="CredentialMode"/> from DI / route defaults.
        /// </summary>
        public ICredentialProvider? CredentialProvider { get; set; }

        /// <summary>Claim-to-header mappings used when <see cref="CredentialMode"/> is <see cref="CredentialMode.ForwardClaims"/>.</summary>
        public List<ClaimHeaderMapping> ClaimMappings { get; } = new List<ClaimHeaderMapping>();
    }
}
