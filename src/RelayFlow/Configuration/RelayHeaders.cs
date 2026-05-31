using System;
using System.Collections.Generic;

namespace RelayFlow.Configuration
{
    /// <summary>
    /// Well-known header handling defaults shared across the relay pipeline.
    /// </summary>
    public static class RelayHeaders
    {
        /// <summary>
        /// Hop-by-hop headers that must never be forwarded between connections (RFC 7230 §6.1
        /// plus related framing headers). These are stripped from both the forwarded request
        /// and the returned response.
        /// </summary>
        public static readonly IReadOnlyCollection<string> HopByHop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Keep-Alive",
            "Proxy-Authenticate",
            "Proxy-Authorization",
            "TE",
            "Trailer",
            "Transfer-Encoding",
            "Upgrade",
            // Common proxy framing headers that should not leak through a relay.
            "Proxy-Connection",
            "Alt-Svc",
        };

        /// <summary>
        /// Response headers that commonly leak internal implementation details and are removed
        /// by default before the response is returned to the public caller.
        /// </summary>
        public static readonly IReadOnlyCollection<string> SensitiveResponseDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Server",
            "X-Powered-By",
            "X-AspNet-Version",
            "X-AspNetMvc-Version",
        };
    }
}
