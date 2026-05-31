using System;
using System.Net;
using System.Net.Http;
using RelayFlow.Configuration;

namespace RelayFlow.Forwarding
{
    /// <summary>
    /// Holds the single, shared <see cref="HttpMessageInvoker"/> used for all upstream relay
    /// calls. It is built on a <see cref="SocketsHttpHandler"/> tuned for proxy workloads:
    /// connection pooling with a bounded lifetime (so DNS changes take effect), no cookies, and
    /// no automatic redirect following (a relay returns upstream redirects to the caller).
    /// <para>
    /// Registered as a singleton; never create one per request.
    /// </para>
    /// </summary>
    public sealed class UpstreamHttpClient : IDisposable
    {
        /// <summary>Creates the upstream client from the configured options.</summary>
        public UpstreamHttpClient(RelayOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var handler = new SocketsHttpHandler
            {
                // Proxies must not follow redirects or store cookies on behalf of callers.
                AllowAutoRedirect = !options.DisableUpstreamRedirects ? true : false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None,
                // Recycle pooled connections so DNS/topology changes are picked up.
                PooledConnectionLifetime = options.PooledConnectionLifetime,
                // Per-request timeout is enforced by YARP's ActivityTimeout; keep handler open.
                ConnectTimeout = TimeSpan.FromSeconds(15),
            };

            // Disposal of timeouts is delegated to YARP per-call; use Timeout.InfiniteTimeSpan
            // on the invoker so the forwarder controls cancellation.
            Invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        }

        /// <summary>
        /// Creates the upstream client over a caller-supplied handler. Use for advanced scenarios
        /// (custom mTLS handler, test servers, etc.). The relay does not modify this handler.
        /// </summary>
        public UpstreamHttpClient(HttpMessageHandler handler, bool disposeHandler = true)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Invoker = new HttpMessageInvoker(handler, disposeHandler);
        }

        /// <summary>The shared invoker passed to <c>IHttpForwarder.SendAsync</c>.</summary>
        public HttpMessageInvoker Invoker { get; }

        /// <inheritdoc />
        public void Dispose() => Invoker.Dispose();
    }
}
