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

`XapiClient` is an unsealed class whose service methods are virtual, with a protected parameterless
constructor for test doubles, and there is no `IXapiClient` interface. This is the pattern of the Azure SDK
clients. The `netstandard2.0` build has no default interface methods, so a member added to an interface after
1.0 would break every fake and decorator that implements it; a virtual method can be added to a class in a
minor version. For the same reason the outbox store is the abstract class `OutboxStore`, not an interface.
(Amended 2026-10-17; the first version had an `IXapiClient` interface that `XapiClient` implemented.)

## Consequences

- The only abstraction is the one .NET already has.
- Code that sends statements depends on `XapiClient`. Tests use the embedded LRS or a class derived from
  `XapiClient`; a method a test double does not override throws `InvalidOperationException`.
- Risk: it is not yet verified that `HttpClient` with a custom handler works in Unity WebGL. A spike after
  the client milestone checks it; if it fails, an `IXapiTransport` is added in a minor version.
