# ADR-0010: LRS hosting

- Status: Accepted
- Date: 2026-10-16

## Context

The LRS is embedded in the user's ASP.NET Core application and must fit its routing, authorization and rate
limiting.

## Decision

- Minimal API endpoints in a group: `MapXapiLrs(prefix)` returns the `RouteGroupBuilder`, so users can add
  `RequireAuthorization`, rate limiting and other conventions.
- Version negotiation and the error format are implemented as endpoint filters.
- Alternate Request Syntax is accepted only for 1.0.x requests and is rewritten in middleware.
- `/about` lists both supported versions.
- No MVC controllers.

## Consequences

- The LRS adds no MVC dependency and composes with the host's own endpoint configuration.
