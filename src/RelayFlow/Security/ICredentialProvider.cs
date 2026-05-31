using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace RelayFlow.Security
{
    /// <summary>
    /// Supplies the credential that the relay attaches to the forwarded (upstream) request,
    /// after the inbound caller has already been authenticated by ASP.NET Core.
    /// <para>
    /// Implementations are resolved from DI, so a service token provider can cache/refresh
    /// tokens, read from a secret store, etc. The inbound <c>Authorization</c> header and any
    /// spoofable trusted headers are removed by the relay before this runs.
    /// </para>
    /// </summary>
    public interface ICredentialProvider
    {
        /// <summary>
        /// Applies the upstream credential to <paramref name="upstreamRequest"/>. The current
        /// <see cref="HttpContext"/> is provided so claims/route data can inform the credential
        /// (e.g. tenant-specific keys). Implementations must not throw for the common path;
        /// failures should be surfaced via the relay's error handling.
        /// </summary>
        ValueTask ApplyAsync(HttpContext context, System.Net.Http.HttpRequestMessage upstreamRequest, CancellationToken cancellationToken);
    }
}
