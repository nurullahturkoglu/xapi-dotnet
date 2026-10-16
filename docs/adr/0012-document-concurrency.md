# ADR-0012: Document concurrency

- Status: Accepted
- Date: 2026-10-16

## Context

State, Activity Profile and Agent Profile documents use ETags and `If-Match` / `If-None-Match`. 2.0 dropped
the SHA-1 ETag rule and removed the 1.0.3 exemption that let State `PUT` skip preconditions.

## Decision

- The ETag is the SHA-1 of the content in lower-case hex, computed on write, stored, and used as the EF Core
  concurrency token. (Amended 2026-10-17; the first version chose a random or version-based token.)
- `PUT`, `POST` and `DELETE` read, check the preconditions and write inside one transaction (serializable;
  `BEGIN IMMEDIATE` on SQLite), retried when a concurrent write wins.
- A `PUT` without `If-Match` or `If-None-Match` on an existing document returns 409 under 2.0 on all three
  resources. Under 1.0.3 the State Resource is exempt, and a profile `PUT` without either header returns 409
  when the document exists and 400 when it does not. `POST` and `DELETE` never require a header.
- A missing registration is stored as the key `Guid.Empty`, so "no registration" is its own key.

The ETag amendment: xAPI 1.0.3 (Communication 3.1) requires the ETag of a document GET to be the SHA-1 of the
content, and 2.0 (IEEE 9274.1.1, 4.1.4) only requires a strong, quoted ETag. One SHA-1 value satisfies both
versions without a per-version representation. Two writes of identical content get the same ETag, which is
correct for a strong validator: the representation is the same.

## Consequences

- Concurrent writers cannot lose updates silently.
- Precondition-less `POST` and `DELETE` are accepted even though 2.0 asks Learning Record Providers to send
  `If-Match`: the conformance suite relies on them, and the LRS requirements in 4.1.4 only cover `PUT`.
- 428 Precondition Required is never returned; neither version lists it among the error codes.
- Behaviour differs by version exactly where the standards differ.
