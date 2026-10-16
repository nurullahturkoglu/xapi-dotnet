# ADR-0012: Document concurrency

- Status: Accepted
- Date: 2026-10-16

## Context

State, Activity Profile and Agent Profile documents use ETags and `If-Match` / `If-None-Match`. 2.0 dropped
the SHA-1 ETag rule and removed the 1.0.3 exemption that let State `PUT` skip preconditions.

## Decision

- The ETag is a stored value (a quoted random or version-based string, not a SHA-1 of the content) and is
  the EF Core concurrency token.
- `PUT`, `POST` and `DELETE` compare and swap inside a transaction.
- A State `PUT` without a precondition header returns 409 under 2.0 and is allowed under 1.0.3.
- A missing registration is stored as the key `Guid.Empty`, so "no registration" is its own key.

## Consequences

- Concurrent writers cannot lose updates silently.
- Behaviour differs by version exactly where the standards differ.
