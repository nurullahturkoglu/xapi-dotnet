# ADR-0013: Authentication

- Status: Accepted
- Date: 2026-10-16

## Context

2.0 removed the authentication text from the standard. Embedded deployments need something that works out of
the box, and larger hosts already have their own authentication.

## Decision

- A built-in list of Basic credentials: key, hash of the secret, and scopes.
- Optionally, delegate to an ASP.NET Core authentication scheme (`o.UseAuthenticationScheme("Bearer")`),
  with a mapper from the principal to the authority agent.
- Scopes in v1: `all`, `all/read`, `statements/write`, `statements/read`, `statements/read/mine`, `state`,
  `profile`, `define`.
- The authority is generated automatically.
- No OAuth 1.0.

## Consequences

- Secrets are never stored in plain text.
- Hosts with existing identity infrastructure reuse it instead of managing a second credential store.
