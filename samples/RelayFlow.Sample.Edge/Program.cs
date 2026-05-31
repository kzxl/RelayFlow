using System.Security.Claims;
using RelayFlow;
using RelayFlow.Configuration;
using RelayFlow.Security;

var builder = WebApplication.CreateBuilder(args);

// --- Authentication/authorization at the edge (demo) ---
// In a real app use JWT bearer; here a trivial header scheme keeps the sample self-contained.
builder.Services
    .AddAuthentication(DemoAuth.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DemoAuthHandler>(
        DemoAuth.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- RelayFlow: register with an SSRF allow-list and a service-token credential ---
builder.Services.AddRelayFlow(options =>
{
    // Deny-by-default: only this internal origin may be forwarded to.
    options.Destinations.AllowOrigin("http://localhost:5099");
});

// The service token injected at the edge in place of the user's public token.
builder.Services.AddSingleton<ICredentialProvider>(
    new ServiceTokenCredentialProvider("internal-service-token-123"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// --- Internal API stub (would live in a separate process/network in production) ---
// Echoes what it received so the relay's behavior is observable.
app.MapGet("/orders/{id}", (string id, HttpContext ctx) => Results.Json(new
{
    id,
    receivedAuthorization = ctx.Request.Headers.Authorization.ToString(),
    receivedUser = ctx.Request.Headers["X-Relay-User-Id"].ToString(),
}));

// --- Edge relay endpoint ---
// Public callers hit /api/orders/{id}; after auth, RelayFlow forwards to the internal stub,
// swapping the user's token for the service token and forwarding the user id claim.
app.MapRelay("/api/orders/{id}", "http://localhost:5099/orders/{id}")
   .RequireAuthorization()
   .SwapCredential(CredentialMode.ServiceToken);

// Claims-forwarding endpoint: projects the authenticated "sub" claim into a trusted header.
// Any client-supplied X-Relay-User-Id is stripped first, preventing spoofing.
app.MapRelay("/api/profile/{id}", "http://localhost:5099/orders/{id}")
   .RequireAuthorization()
   .ForwardClaims(new ClaimHeaderMapping("sub", "X-Relay-User-Id"));

// Misconfigured endpoint: destination is NOT on the allow-list, so RelayFlow denies it (502).
app.MapRelay("/api/leaky/{id}", "http://evil.example.com:9999/{id}")
   .RequireAuthorization();

app.Run();

internal static class DemoAuth
{
    public const string SchemeName = "Demo";
}

/// <summary>Trivial demo auth: any request with header <c>X-Demo-User</c> is authenticated.</summary>
internal sealed class DemoAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    public DemoAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Demo-User"].ToString();
        if (string.IsNullOrEmpty(user))
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("No X-Demo-User header."));

        var claims = new[] { new Claim("sub", user), new Claim(ClaimTypes.NameIdentifier, user) };
        var identity = new ClaimsIdentity(claims, DemoAuth.SchemeName);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
            new ClaimsPrincipal(identity), DemoAuth.SchemeName);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}

/// <summary>Exposed so the test project can host this app via WebApplicationFactory.</summary>
public partial class Program { }
