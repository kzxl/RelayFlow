using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace RelayFlow.Security
{
    /// <summary>
    /// A credential provider that does nothing, used for <see cref="Configuration.CredentialMode.None"/>
    /// and <see cref="Configuration.CredentialMode.PassThrough"/> (where header handling is done
    /// by the transform, not by injecting a new credential).
    /// </summary>
    public sealed class NoopCredentialProvider : ICredentialProvider
    {
        /// <summary>Singleton instance.</summary>
        public static readonly NoopCredentialProvider Instance = new NoopCredentialProvider();

        private NoopCredentialProvider() { }

        /// <inheritdoc />
        public ValueTask ApplyAsync(HttpContext context, HttpRequestMessage upstreamRequest, CancellationToken cancellationToken)
            => default;
    }

    /// <summary>
    /// Attaches a bearer token to the upstream request. The token is produced by a delegate so
    /// it can be cached or refreshed from a secret store / token service.
    /// </summary>
    public sealed class ServiceTokenCredentialProvider : ICredentialProvider
    {
        private readonly Func<HttpContext, CancellationToken, ValueTask<string>> _tokenFactory;

        /// <summary>Creates a provider that resolves the token via <paramref name="tokenFactory"/>.</summary>
        public ServiceTokenCredentialProvider(Func<HttpContext, CancellationToken, ValueTask<string>> tokenFactory)
        {
            _tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
        }

        /// <summary>Creates a provider that always uses a fixed token (e.g. from configuration).</summary>
        public ServiceTokenCredentialProvider(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token required.", nameof(token));
            _tokenFactory = (_, _) => new ValueTask<string>(token);
        }

        /// <inheritdoc />
        public async ValueTask ApplyAsync(HttpContext context, HttpRequestMessage upstreamRequest, CancellationToken cancellationToken)
        {
            string token = await _tokenFactory(context, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                upstreamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
    }

    /// <summary>
    /// Attaches a static API key header (e.g. <c>X-Api-Key: ...</c>) to the upstream request.
    /// </summary>
    public sealed class ApiKeyCredentialProvider : ICredentialProvider
    {
        private readonly string _headerName;
        private readonly Func<HttpContext, CancellationToken, ValueTask<string>> _valueFactory;

        /// <summary>Creates a provider using a fixed key value.</summary>
        public ApiKeyCredentialProvider(string headerName, string value)
        {
            if (string.IsNullOrWhiteSpace(headerName)) throw new ArgumentException("Header name required.", nameof(headerName));
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Value required.", nameof(value));
            _headerName = headerName;
            _valueFactory = (_, _) => new ValueTask<string>(value);
        }

        /// <summary>Creates a provider that resolves the key per request.</summary>
        public ApiKeyCredentialProvider(string headerName, Func<HttpContext, CancellationToken, ValueTask<string>> valueFactory)
        {
            if (string.IsNullOrWhiteSpace(headerName)) throw new ArgumentException("Header name required.", nameof(headerName));
            _headerName = headerName;
            _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        }

        /// <inheritdoc />
        public async ValueTask ApplyAsync(HttpContext context, HttpRequestMessage upstreamRequest, CancellationToken cancellationToken)
        {
            string value = await _valueFactory(context, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(value))
            {
                upstreamRequest.Headers.TryAddWithoutValidation(_headerName, value);
            }
        }
    }
}
