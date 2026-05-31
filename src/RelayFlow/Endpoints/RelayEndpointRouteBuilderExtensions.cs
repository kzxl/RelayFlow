using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RelayFlow.Configuration;
using RelayFlow.Forwarding;

namespace RelayFlow
{
    /// <summary>
    /// Minimal-API extensions for mapping relay endpoints.
    /// </summary>
    public static class RelayEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps a relay endpoint: requests matching <paramref name="pattern"/> are forwarded to
        /// the resolved <paramref name="destinationTemplate"/> after the endpoint's
        /// authorization (if any) has run.
        /// <para>
        /// Apply <c>RequireAuthorization(...)</c> and the RelayFlow fluent methods (e.g.
        /// <see cref="RelayEndpointConventionBuilder.SwapCredential"/>) on the returned builder.
        /// </para>
        /// </summary>
        /// <param name="endpoints">The endpoint route builder.</param>
        /// <param name="pattern">Public route pattern, e.g. <c>/api/orders/{**rest}</c>.</param>
        /// <param name="destinationTemplate">Upstream template, e.g. <c>https://internal:5000/orders/{**rest}</c>.</param>
        public static RelayEndpointConventionBuilder MapRelay(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            string destinationTemplate)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            var route = new RelayRoute(pattern, destinationTemplate);

            var builder = endpoints.Map(pattern, async (HttpContext context) =>
            {
                var forwarder = context.RequestServices.GetRequiredService<RelayForwarder>();
                await forwarder.RelayAsync(context, route);
            });

            return new RelayEndpointConventionBuilder(builder, route);
        }
    }
}
