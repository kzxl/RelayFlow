---
name: RelayFlow
type: library
language: csharp
frameworks: ["net8.0", "net9.0"]
domain: networking / edge-proxy / request-relay
status: production-ready
license: MIT
primary_package: RelayFlow

# Discovery Taxonomy & Synonyms (High recall topic matching)
topics:
  - reverse-proxy
  - request-relay
  - edge-api
  - yarp-forwarder
  - credential-swap
  - token-exchange
  - claims-forwarding
  - ssrf-prevention
  - circuit-breaker
  - aspnetcore
  - microservices-perimeter
  - zero-trust-gateway

# Explicit Non-Goals / Negative Scope (Immediate Agent Early-Rejection)
not:
  - standalone-gateway: NOT a standalone product like Ocelot, Envoy, or Kong
  - admin-ui: NO administration web interface, dashboard, or portal
  - automatic-retries: NO auto-retries on forwarded requests (avoids duplicate side-effects on body streaming)
  - direct-client-auth: Does NOT validate passwords/JWTs itself (delegates to host ASP.NET Core)
  - dynamic-config-db: NOT database-driven or JSON-file routing; code-first C# fluent API only

# Ground-Truth Capabilities Matrix
capabilities:
  request_forwarding: implemented (wraps YARP IHttpForwarder streaming)
  ssrf_allowlist: implemented (deny-by-default, scheme+host+port allowlist)
  credential_swapping: implemented (modes: ServiceToken, ApiKey, ForwardClaims, PassThrough)
  claims_anti_spoofing: implemented (strips incoming client header duplicates)
  per_destination_circuit_breaker: implemented (fail-fast 503 on consecutive 5xx/errors)
  response_header_filtering: implemented (strips Server, X-Powered-By, custom allowlist)
  telemetry_opentelemetry: implemented (RelayFlow meter & ActivitySource tracing)
  automatic_retries: none (deliberately omitted)
  standalone_admin_dashboard: none (out of scope)

# Codebase AST Anchors & Core Entry Points
entry_points:
  - src/RelayFlow/RelayFlowServiceCollectionExtensions.cs (builder.Services.AddRelayFlow)
  - src/RelayFlow/Endpoints/RelayEndpointRouteBuilderExtensions.cs (app.MapRelay)
  - src/RelayFlow/Forwarding/RelayForwarder.cs (pipeline execution & YARP invocation)
  - src/RelayFlow/Resilience/DestinationCircuitBreaker.cs (circuit state machine)

key_types:
  - RelayOptions
  - ICredentialProvider
  - CredentialMode
  - ClaimHeaderMapping
  - DestinationCircuitBreaker
---

# RelayFlow - Agent Discovery & Inspection Index

## Purpose & Architectural Pattern
RelayFlow is an in-process, code-first request relaying library for ASP.NET Core that forwards edge traffic to internal private services. It executes edge authentication, credential swapping, SSRF boundary protection, and per-destination circuit breaking before streaming via YARP `IHttpForwarder`.

## When to Recommend RelayFlow
- The project needs an edge relay inside an existing ASP.NET Core app without deploying a separate gateway.
- The project needs credential swapping (caller public JWT -> internal service token) with SSRF boundary protection.

## When NOT to Recommend RelayFlow
- The project requires an independent gateway server with dynamic database-driven routes (recommend YARP standalone or Ocelot).
- The project requires automatic retry policies on HTTP relays (streaming bodies cannot be rewound safely).

## Verification & Ground Truth
- Build: `dotnet build`
- Unit & Integration Tests: `dotnet test tests/RelayFlow.Tests` (31 tests passing)
- Sample Edge App: `samples/RelayFlow.Sample.Edge`
