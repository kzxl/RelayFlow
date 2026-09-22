# RelayFlow

> [!NOTE]
> **AI / Agent Discovery Index**: For machine-readable capabilities, non-goals, and AST entry points, see [AGENTS.md](AGENTS.md) or [llms.txt](llms.txt).

A lightweight, **code-first request-relaying** library for ASP.NET Core. Forward
selected endpoints from a public **edge API** to an **internal API** after applying
your own authentication and authorization, swapping credentials at the boundary,
with secure defaults.

RelayFlow wraps YARP's `IHttpForwarder`, so the hard parts (byte streaming,
hop-by-hop headers, cancellation) are handled by battle-tested Microsoft code. The
value RelayFlow adds is the *ergonomics and the security model* for the specific
"authenticate at the edge, then forward to internal" pattern.

## The scenario

You have an internal API. You want to expose part of it to web/mobile clients
without rewriting it, and without exposing it directly:

```
[Web / Mobile]  --(public, JWT)-->  [Edge API + RelayFlow]  --(internal cred)-->  [Internal API]
                                          │
                                          ├─ 1. Authenticate + Authorize (ASP.NET Core)
                                          ├─ 2. Check destination against SSRF allow-list
                                          ├─ 3. Strip spoofable headers / inbound token
                                          ├─ 4. Swap in an internal credential
                                          └─ 5. Forward (YARP IHttpForwarder), filter response
```

## When to use RelayFlow vs YARP vs a gateway

- **RelayFlow**: you want to relay *a few specific endpoints* from *inside your
  existing ASP.NET Core app*, code-first, reusing your `[Authorize]` policies, and
  you need credential swapping at the edge. Embeddable library, not a separate app.
- **YARP (`MapReverseProxy`)**: you want a config/DB-driven reverse proxy with
  routes/clusters, load balancing, and health checks. RelayFlow uses YARP's lower-
  level `IHttpForwarder` primitive rather than its high-level pipeline.
- **A full gateway (Ocelot, a YARP-based gateway app)**: you want a standalone
  product with an admin UI, dynamic config, token issuance, and dashboards.

If you already run a gateway for north-south traffic, RelayFlow is still useful for
*selective, in-process relays* with custom edge logic.

## Install

```
dotnet add package RelayFlow
```

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(/* your JWT bearer, etc. */);
builder.Services.AddAuthorization();

builder.Services.AddRelayFlow(options =>
{
    // REQUIRED: deny-by-default SSRF allow-list. Without this, every relay is denied.
    options.Destinations.AllowOrigin("https://internal-api:5000");
});

// Provide the internal credential used at the edge.
builder.Services.AddSingleton<ICredentialProvider>(
    new ServiceTokenCredentialProvider("internal-service-token"));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Forward GET/POST/... /api/orders/** to the internal API, after authorization,
// swapping the caller's token for the internal service token.
app.MapRelay("/api/orders/{**rest}", "https://internal-api:5000/orders/{**rest}")
   .RequireAuthorization("OrdersPolicy")
   .SwapCredential(CredentialMode.ServiceToken)
   .AllowResponseHeaders("Content-Type", "Cache-Control");

app.Run();
```

## Credential modes

`SwapCredential(CredentialMode)` controls what is sent upstream after the caller is
authenticated:

| Mode | Behavior |
|------|----------|
| `None` (default) | Strip the inbound `Authorization`; send nothing. Safest; relies on a trusted network (private subnet / mTLS). |
| `ServiceToken` | Replace with a bearer token from a registered `ICredentialProvider`. |
| `ApiKey` | Attach a static API-key header from a registered `ICredentialProvider`. |
| `ForwardClaims` | Project selected user claims into trusted headers (see below). |
| `PassThrough` | Forward the original `Authorization` unchanged (opt-in; only if internal validates the same token). |

### Forwarding claims to trusted headers

```csharp
app.MapRelay("/api/profile/{id}", "https://internal-api:5000/users/{id}")
   .RequireAuthorization()
   .ForwardClaims(
       new ClaimHeaderMapping("sub", "X-Relay-User-Id"),
       new ClaimHeaderMapping("tenant", "X-Relay-Tenant"));
```

RelayFlow strips any client-supplied `X-Relay-User-Id` / `X-Relay-Tenant` **before**
setting them from the authenticated principal, so the internal API can trust them.

## Security model

RelayFlow is secure-by-default. The defaults exist because these are the mistakes
hand-rolled forwarders commonly make:

- **SSRF allow-list (deny-by-default).** Forwarding is only permitted to registered
  origins (scheme + host + port; wildcard subdomains supported). A destination
  derived from request input can never reach an arbitrary host. With no origins
  configured, every relay returns `502`.
- **Header spoofing prevention.** Client-supplied copies of forwarded-claim headers
  are stripped before they are (re)set from the authenticated principal.
- **Token isolation.** The inbound public `Authorization` is removed before
  forwarding unless `PassThrough` is explicitly chosen.
- **Hop-by-hop hygiene.** `Connection`, `Transfer-Encoding`, `Upgrade`, `TE`,
  `Trailer`, `Proxy-*`, etc. are not forwarded (handled by YARP + RelayFlow).
- **Response header filtering.** Sensitive headers (`Server`, `X-Powered-By`, ...)
  are stripped; an optional `AllowResponseHeaders(...)` allow-list removes the rest.
- **No detail leakage.** Upstream/forwarding errors are logged server-side and
  surfaced as plain gateway status codes, never as internal stack traces.

> RelayFlow does not authenticate callers itself; it relies on ASP.NET Core
> authentication/authorization that you configure and attach via `RequireAuthorization`.

## Resilience: per-destination circuit breaker

When an internal API starts failing, blindly forwarding every request piles load
onto a service that is already struggling. RelayFlow trips a circuit breaker per
destination authority (`scheme://host:port`): after a configured number of
consecutive failures (forwarding errors or upstream `5xx`), the circuit opens and
the relay **fails fast with `503`** without contacting the internal service. After a
cooldown it allows a single trial request; success closes the circuit, failure
re-opens it.

```csharp
builder.Services.AddRelayFlow(options =>
{
    options.Destinations.AllowOrigin("https://internal-api:5000");

    options.CircuitBreaker.Enabled = true;          // default
    options.CircuitBreaker.FailureThreshold = 5;     // consecutive failures to trip
    options.CircuitBreaker.Cooldown = TimeSpan.FromSeconds(10);
});
```

Upstream `4xx` responses are the caller's fault and do **not** count as failures.

### Why no automatic retries?

RelayFlow deliberately does not auto-retry forwarded requests. A relay streams the
request body upstream; once streaming has begun the body cannot be safely rewound,
and retrying non-idempotent methods (`POST`/`PATCH`) risks duplicate side effects.
Retrying belongs at the client (idempotent calls) or behind an idempotency key, not
blindly in a proxy. The circuit breaker provides the safe, proxy-appropriate
resilience primitive instead.

## Observability

RelayFlow emits standard .NET telemetry with no extra dependency, so OpenTelemetry
or `dotnet-counters` can collect it:

- **Metrics** (meter `RelayFlow`): `relayflow.requests` (by destination + status),
  `relayflow.rejected` (by reason: `ssrf_denied`, `circuit_open`, ...), and
  `relayflow.duration` (ms histogram).
- **Tracing** (activity source `RelayFlow`): one `relayflow.forward` span per request
  with destination, method, and status-code tags.

```csharp
// OpenTelemetry wiring (example)
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("RelayFlow"))
    .WithTracing(t => t.AddSource("RelayFlow"));
```

## Fluent API

| Method | Purpose |
|--------|---------|
| `MapRelay(pattern, destinationTemplate)` | Maps a relay endpoint. Templates support `{name}` and `{**catchAll}`. |
| `.RequireAuthorization(...)` | Standard ASP.NET Core authorization (policies, roles). |
| `.SwapCredential(mode)` | Set the credential mode. |
| `.WithCredentialProvider(provider)` | Use an explicit provider instance. |
| `.ForwardClaims(mappings...)` | Project claims into trusted headers (anti-spoof). |
| `.AllowResponseHeaders(names...)` | Restrict returned response headers. |
| `.StripRequestHeaders(names...)` | Remove extra request headers before forwarding. |
| `.WithTimeout(timeSpan)` | Per-route upstream timeout. |

## How forwarding works

`RelayForwarder` resolves the destination from the template + route values, enforces
the SSRF allow-list, builds a per-route `HttpTransformer` (credential swap + header
hygiene), then calls `IHttpForwarder.SendAsync` with a single shared, pooled
`SocketsHttpHandler` (bounded connection lifetime, no cookies, no auto-redirect).

## Build & test

```
dotnet build
dotnet test
```

The test suite (31 tests) covers the SSRF policy, destination resolver, and circuit
breaker state machine as units, validates metric emission via a `MeterListener`, and
runs end-to-end relay scenarios over the sample edge app via `WebApplicationFactory`
(token swap, claims projection, spoof-stripping, SSRF deny, and circuit-breaker
fail-fast).

## Sample

`samples/RelayFlow.Sample.Edge` is a single self-contained app: an internal stub
endpoint plus edge relay endpoints demonstrating credential swap, claims forwarding,
and a deliberately misconfigured (denied) destination.

## License

MIT. See [LICENSE](LICENSE).
