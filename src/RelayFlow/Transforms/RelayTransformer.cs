using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using RelayFlow.Configuration;
using RelayFlow.Security;
using Yarp.ReverseProxy.Forwarder;

namespace RelayFlow.Transforms
{
    /// <summary>
    /// A YARP <see cref="HttpTransformer"/> that applies RelayFlow's security model to the
    /// forwarded request and the returned response:
    /// <list type="bullet">
    ///   <item>Strips hop-by-hop headers (handled by the base) and configured request headers.</item>
    ///   <item>Removes the inbound <c>Authorization</c> unless pass-through is configured.</item>
    ///   <item>Removes client-supplied copies of trusted headers before they are (re)set, to
    ///         prevent spoofing.</item>
    ///   <item>Applies the configured <see cref="ICredentialProvider"/> to inject upstream credentials.</item>
    ///   <item>Filters response headers (sensitive defaults and optional allow-list).</item>
    /// </list>
    /// </summary>
    internal sealed class RelayTransformer : HttpTransformer
    {
        private readonly RelayRoute _route;
        private readonly RelayOptions _options;
        private readonly ICredentialProvider _credentialProvider;
        private readonly IReadOnlyCollection<string> _trustedHeaderNames;
        private readonly Uri _destination;

        public RelayTransformer(
            RelayRoute route,
            RelayOptions options,
            ICredentialProvider credentialProvider,
            IReadOnlyCollection<string> trustedHeaderNames,
            Uri destination)
        {
            _route = route;
            _options = options;
            _credentialProvider = credentialProvider;
            _trustedHeaderNames = trustedHeaderNames;
            _destination = destination;
        }

        public override async ValueTask TransformRequestAsync(
            HttpContext httpContext,
            HttpRequestMessage proxyRequest,
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            // Base copies most headers and strips hop-by-hop ones, and sets the request URI.
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken)
                .ConfigureAwait(false);

            // Override with the exact resolved destination (the template may rewrite the path,
            // which the base transformer cannot know about; it only appends the request path).
            proxyRequest.RequestUri = _destination;

            // Remove the inbound public credential so it never reaches the internal API,
            // unless the route explicitly wants to pass it through.
            if (_options.StripInboundAuthorization && _route.CredentialMode != CredentialMode.PassThrough)
            {
                proxyRequest.Headers.Authorization = null;
            }

            // Strip client-supplied copies of trusted headers to prevent spoofing
            // (e.g. a client sending its own X-Relay-User-Id).
            foreach (var header in _trustedHeaderNames)
            {
                proxyRequest.Headers.Remove(header);
                if (proxyRequest.Content != null)
                {
                    proxyRequest.Content.Headers.Remove(header);
                }
            }

            // Strip any additional route-configured request headers.
            foreach (var header in _route.StripRequestHeaders)
            {
                proxyRequest.Headers.Remove(header);
                proxyRequest.Content?.Headers.Remove(header);
            }

            // Inject the upstream credential (service token / api key / claims).
            await _credentialProvider.ApplyAsync(httpContext, proxyRequest, cancellationToken)
                .ConfigureAwait(false);
        }

        public override ValueTask<bool> TransformResponseAsync(
            HttpContext httpContext,
            HttpResponseMessage? proxyResponse,
            CancellationToken cancellationToken)
        {
            // Let the base copy the status code and headers first.
            var baseResult = base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);

            // Strip sensitive/implementation-detail response headers.
            if (_options.StripSensitiveResponseHeaders)
            {
                foreach (var header in RelayHeaders.SensitiveResponseDefaults)
                {
                    httpContext.Response.Headers.Remove(header);
                }
            }

            // If an allow-list is set, remove every response header not on it (keeping framing
            // essentials that the server controls).
            if (_route.ResponseHeaderAllowList != null)
            {
                var toRemove = new List<string>();
                foreach (var header in httpContext.Response.Headers.Keys)
                {
                    if (!_route.ResponseHeaderAllowList.Contains(header)
                        && !IsFramingEssential(header))
                    {
                        toRemove.Add(header);
                    }
                }
                foreach (var header in toRemove)
                {
                    httpContext.Response.Headers.Remove(header);
                }
            }

            return baseResult;
        }

        private static bool IsFramingEssential(string header) =>
            header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            || header.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            || header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase);
    }
}
