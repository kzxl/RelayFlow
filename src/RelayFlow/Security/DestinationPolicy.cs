using System;
using System.Collections.Generic;
using System.Linq;

namespace RelayFlow.Security
{
    /// <summary>
    /// Result of evaluating a candidate forwarding destination against the policy.
    /// </summary>
    public readonly struct DestinationDecision
    {
        private DestinationDecision(bool allowed, string? reason)
        {
            IsAllowed = allowed;
            Reason = reason;
        }

        /// <summary>True if the destination is permitted.</summary>
        public bool IsAllowed { get; }

        /// <summary>When denied, a short machine-safe reason (never echoed to clients verbatim).</summary>
        public string? Reason { get; }

        /// <summary>Creates an allowed decision.</summary>
        public static DestinationDecision Allow() => new DestinationDecision(true, null);

        /// <summary>Creates a denied decision with a reason.</summary>
        public static DestinationDecision Deny(string reason) => new DestinationDecision(false, reason);
    }

    /// <summary>
    /// An allow-list policy that decides whether the relay is permitted to forward to a given
    /// absolute destination URI. This is the primary defense against Server-Side Request Forgery
    /// (SSRF): a relay must never forward to an arbitrary host derived from untrusted input.
    /// <para>
    /// The policy is deny-by-default. A destination is allowed only if its scheme, host, and
    /// port match a registered allowed origin. Hosts are matched case-insensitively; an exact
    /// host or a wildcard subdomain pattern (e.g. <c>*.internal.example.com</c>) may be used.
    /// </para>
    /// </summary>
    public sealed class DestinationPolicy
    {
        private readonly List<AllowedOrigin> _allowed = new List<AllowedOrigin>();

        /// <summary>True when no origins are registered (everything is denied).</summary>
        public bool IsEmpty => _allowed.Count == 0;

        /// <summary>
        /// Registers an allowed origin from an absolute base URL, e.g.
        /// <c>https://internal-api:5000</c>. Only the scheme/host/port are used; any path is
        /// ignored for matching.
        /// </summary>
        public DestinationPolicy AllowOrigin(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                throw new ArgumentException($"'{baseUrl}' is not an absolute URL.", nameof(baseUrl));

            _allowed.Add(new AllowedOrigin(uri.Scheme, uri.Host, uri.Port));
            return this;
        }

        /// <summary>
        /// Registers an allowed origin from explicit parts. Use <c>"*."</c>-prefixed
        /// <paramref name="host"/> for wildcard subdomain matching. A <paramref name="port"/> of
        /// <c>-1</c> matches the default port for the scheme.
        /// </summary>
        public DestinationPolicy AllowOrigin(string scheme, string host, int port = -1)
        {
            if (string.IsNullOrWhiteSpace(scheme)) throw new ArgumentException("Scheme required.", nameof(scheme));
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host required.", nameof(host));

            _allowed.Add(new AllowedOrigin(scheme, host, port));
            return this;
        }

        /// <summary>
        /// Evaluates a destination URI against the allow-list. The URI must be absolute.
        /// </summary>
        public DestinationDecision Evaluate(Uri destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!destination.IsAbsoluteUri)
                return DestinationDecision.Deny("destination_not_absolute");

            if (_allowed.Count == 0)
                return DestinationDecision.Deny("no_allowed_origins_configured");

            // Only http/https are ever relayable; reject file://, gopher://, etc. used in SSRF.
            if (!IsHttpScheme(destination.Scheme))
                return DestinationDecision.Deny("scheme_not_allowed");

            foreach (var origin in _allowed)
            {
                if (origin.Matches(destination))
                    return DestinationDecision.Allow();
            }

            return DestinationDecision.Deny("destination_not_in_allow_list");
        }

        private static bool IsHttpScheme(string scheme) =>
            scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        private readonly struct AllowedOrigin
        {
            private readonly string _scheme;
            private readonly string _host;
            private readonly int _port;
            private readonly bool _wildcard;
            private readonly string _wildcardSuffix;

            public AllowedOrigin(string scheme, string host, int port)
            {
                _scheme = scheme;
                _port = port;
                if (host.StartsWith("*.", StringComparison.Ordinal))
                {
                    _wildcard = true;
                    // "*.internal.example.com" -> suffix ".internal.example.com"
                    _wildcardSuffix = host.Substring(1);
                    _host = host;
                }
                else
                {
                    _wildcard = false;
                    _wildcardSuffix = string.Empty;
                    _host = host;
                }
            }

            public bool Matches(Uri destination)
            {
                if (!destination.Scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase))
                    return false;

                int effectivePort = _port == -1 ? DefaultPort(_scheme) : _port;
                if (destination.Port != effectivePort)
                    return false;

                if (_wildcard)
                {
                    // Must be a strict subdomain: "a.internal.example.com" ends with
                    // ".internal.example.com" but the bare apex is not matched by "*.".
                    return destination.Host.EndsWith(_wildcardSuffix, StringComparison.OrdinalIgnoreCase)
                           && destination.Host.Length > _wildcardSuffix.Length;
                }

                return destination.Host.Equals(_host, StringComparison.OrdinalIgnoreCase);
            }

            private static int DefaultPort(string scheme) =>
                scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        }
    }
}
