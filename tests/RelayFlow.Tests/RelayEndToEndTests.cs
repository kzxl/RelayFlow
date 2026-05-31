using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RelayFlow.Forwarding;
using Xunit;

namespace RelayFlow.Tests
{
    /// <summary>
    /// End-to-end relay tests over the sample edge app. The relay's upstream client is replaced
    /// with the in-memory <c>TestServer</c> handler, so a forward to <c>http://localhost:5099</c>
    /// routes back into the same server, which also hosts the internal stub endpoint.
    /// </summary>
    public class RelayEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public RelayEndToEndTests(WebApplicationFactory<Program> factory)
        {
            // Capture the base factory so the test-services lambda can route the relay's
            // upstream calls back into that factory's in-memory server.
            var baseFactory = factory;
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Route the relay's upstream calls back into the in-memory test server.
                    services.RemoveAll<UpstreamHttpClient>();
                    services.AddSingleton(sp =>
                    {
                        var handler = baseFactory.Server.CreateHandler();
                        return new UpstreamHttpClient(handler, disposeHandler: false);
                    });
                });
            });
        }

        private HttpClient AuthedClient()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Demo-User", "alice");
            return client;
        }

        [Fact]
        public async Task Unauthenticated_Request_Is401()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/orders/42");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Authenticated_Request_IsForwarded_AndReturnsUpstreamBody()
        {
            var client = AuthedClient();
            var response = await client.GetAsync("/api/orders/42");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<EchoResponse>();
            Assert.NotNull(body);
            Assert.Equal("42", body!.Id);
        }

        [Fact]
        public async Task UserToken_IsSwapped_ForServiceToken()
        {
            var client = AuthedClient();
            // Send a public bearer token; it must NOT reach the internal stub.
            client.DefaultRequestHeaders.Add("Authorization", "Bearer public-user-token");

            var response = await client.GetAsync("/api/orders/7");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<EchoResponse>();

            Assert.NotNull(body);
            // The internal stub should have seen the service token, not the public token.
            Assert.Equal("Bearer internal-service-token-123", body!.ReceivedAuthorization);
            Assert.DoesNotContain("public-user-token", body.ReceivedAuthorization);
        }

        [Fact]
        public async Task ForwardClaims_ProjectsAuthenticatedClaim_ToTrustedHeader()
        {
            var client = AuthedClient(); // X-Demo-User: alice -> "sub" claim = alice
            var response = await client.GetAsync("/api/profile/99");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<EchoResponse>();
            Assert.NotNull(body);
            Assert.Equal("alice", body!.ReceivedUser);
        }

        [Fact]
        public async Task ForwardClaims_StripsClientSuppliedTrustedHeader_PreventingSpoof()
        {
            var client = AuthedClient(); // authenticated as alice
            // Attacker tries to impersonate "admin" by sending the trusted header directly.
            client.DefaultRequestHeaders.Add("X-Relay-User-Id", "admin");

            var response = await client.GetAsync("/api/profile/99");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<EchoResponse>();

            Assert.NotNull(body);
            // The spoofed value must be stripped and replaced with the real authenticated claim.
            Assert.Equal("alice", body!.ReceivedUser);
            Assert.NotEqual("admin", body.ReceivedUser);
        }

        [Fact]
        public async Task DestinationNotOnAllowList_IsDenied_With502()
        {
            var client = AuthedClient();
            var response = await client.GetAsync("/api/leaky/1");
            // SSRF guard denies the forward; no request is made to the disallowed host.
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        private sealed class EchoResponse
        {
            public string Id { get; set; } = "";
            public string ReceivedAuthorization { get; set; } = "";
            public string ReceivedUser { get; set; } = "";
        }
    }
}
