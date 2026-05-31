using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace RelayFlow.Security
{
    /// <summary>
    /// Projects selected claims of the authenticated user into trusted request headers for the
    /// internal API to consume (e.g. <c>X-Relay-User-Id</c>, <c>X-Relay-Tenant</c>).
    /// <para>
    /// Security: the relay strips any client-supplied copies of these headers before this
    /// provider sets them, so the internal API can trust them. Only claims that are present are
    /// emitted. This is intended for an internal API that trusts the edge over a private link.
    /// </para>
    /// </summary>
    public sealed class ClaimsForwardingCredentialProvider : ICredentialProvider
    {
        private readonly IReadOnlyList<ClaimHeaderMapping> _mappings;

        /// <summary>Creates a provider with the given claim-to-header mappings.</summary>
        public ClaimsForwardingCredentialProvider(IReadOnlyList<ClaimHeaderMapping> mappings)
        {
            _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        /// <summary>The header names this provider writes (so the relay can strip inbound copies).</summary>
        public IEnumerable<string> HeaderNames
        {
            get
            {
                foreach (var m in _mappings) yield return m.HeaderName;
            }
        }

        /// <inheritdoc />
        public ValueTask ApplyAsync(HttpContext context, HttpRequestMessage upstreamRequest, CancellationToken cancellationToken)
        {
            ClaimsPrincipal? user = context.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                foreach (var mapping in _mappings)
                {
                    var claim = user.FindFirst(mapping.ClaimType);
                    if (claim != null && !string.IsNullOrEmpty(claim.Value))
                    {
                        upstreamRequest.Headers.TryAddWithoutValidation(mapping.HeaderName, claim.Value);
                    }
                }
            }
            return default;
        }
    }

    /// <summary>Maps an authenticated claim type to an outbound trusted header name.</summary>
    public readonly struct ClaimHeaderMapping
    {
        /// <summary>Creates a mapping.</summary>
        public ClaimHeaderMapping(string claimType, string headerName)
        {
            if (string.IsNullOrWhiteSpace(claimType)) throw new ArgumentException("Claim type required.", nameof(claimType));
            if (string.IsNullOrWhiteSpace(headerName)) throw new ArgumentException("Header name required.", nameof(headerName));
            ClaimType = claimType;
            HeaderName = headerName;
        }

        /// <summary>The claim type to read from the authenticated principal.</summary>
        public string ClaimType { get; }

        /// <summary>The header name to write upstream.</summary>
        public string HeaderName { get; }
    }
}
