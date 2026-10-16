# ADR-0008: HTTP client

- Status: Accepted
- Date: 2026-10-16

## Context

Unity WebGL has no sockets, so requests have to go through `UnityWebRequest`. Users of dependency injection
expect `IHttpClientFactory` and Polly or Microsoft.Extensions.Http.Resilience. Unity users do not want
Microsoft.Extensions.* dependencies.

## Options

1. Our own `IXapiTransport` abstraction.
2. Transport through `HttpMessageHandler`.

## Decision

Option 2. The client is `XapiClient(HttpClient, XapiClientOptions)`; Unity plugs in a
`UnityWebRequestHandler`. The client has no built-in retry: resilience is documented with
`AddStandardResilienceHandler`, and durable retry lives in the outbox (ADR-0009). The dependency injection
extension exists only in the `net10.0` build.

## Consequences

- The only abstraction is the one .NET already has.
- Risk: it is not yet verified that `HttpClient` with a custom handler works in Unity WebGL. A spike after
  the client milestone checks it; if it fails, an `IXapiTransport` is added in a minor version.
