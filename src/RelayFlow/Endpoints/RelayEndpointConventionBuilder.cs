using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using RelayFlow.Configuration;
using RelayFlow.Security;

namespace RelayFlow
{
    /// <summary>
    /// A fluent builder for configuring a relay endpoint mapped via
    /// <see cref="RelayEndpointRouteBuilderExtensions.MapRelay"/>. Implements
    /// <see cref="IEndpointConventionBuilder"/> so standard conventions such as
    /// <c>RequireAuthorization</c> and <c>WithName</c> can be chained as well.
    /// </summary>
    public sealed class RelayEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly IEndpointConventionBuilder _inner;
        private readonly RelayRoute _route;

        internal RelayEndpointConventionBuilder(IEndpointConventionBuilder inner, RelayRoute route)
        {
            _inner = inner;
            _route = route;
        }

        /// <inheritdoc />
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention) => _inner.Add(convention);

        /// <summary>
        /// Sets how credentials are handled when forwarding. For
        /// <see cref="CredentialMode.ServiceToken"/> / <see cref="CredentialMode.ApiKey"/> an
        /// <see cref="ICredentialProvider"/> must be registered in DI.
        /// </summary>
        public RelayEndpointConventionBuilder SwapCredential(CredentialMode mode)
        {
            _route.CredentialMode = mode;
            return this;
        }

        /// <summary>
        /// Uses an explicit credential provider instance for this route, overriding
        /// <see cref="SwapCredential"/>.
        /// </summary>
        public RelayEndpointConventionBuilder WithCredentialProvider(ICredentialProvider provider)
        {
            _route.CredentialProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        /// Configures claim-to-header forwarding and sets the mode to
        /// <see cref="CredentialMode.ForwardClaims"/>. Client-supplied copies of these headers
        /// are stripped before they are set, preventing spoofing.
        /// </summary>
        public RelayEndpointConventionBuilder ForwardClaims(params ClaimHeaderMapping[] mappings)
        {
            if (mappings == null || mappings.Length == 0)
                throw new ArgumentException("At least one mapping is required.", nameof(mappings));

            _route.CredentialMode = CredentialMode.ForwardClaims;
            _route.ClaimMappings.Clear();
            _route.ClaimMappings.AddRange(mappings);
            return this;
        }

        /// <summary>
        /// Restricts the response headers returned to the caller to the given allow-list (plus
        /// framing essentials). Any other upstream response header is removed.
        /// </summary>
        public RelayEndpointConventionBuilder AllowResponseHeaders(params string[] headerNames)
        {
            _route.ResponseHeaderAllowList = new HashSet<string>(headerNames, StringComparer.OrdinalIgnoreCase);
            return this;
        }

        /// <summary>Adds request header names to strip before forwarding.</summary>
        public RelayEndpointConventionBuilder StripRequestHeaders(params string[] headerNames)
        {
            foreach (var name in headerNames) _route.StripRequestHeaders.Add(name);
            return this;
        }

        /// <summary>Sets a per-route upstream timeout, overriding the global default.</summary>
        public RelayEndpointConventionBuilder WithTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
            _route.Timeout = timeout;
            return this;
        }
    }
}
