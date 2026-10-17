# ADR-0020: Statement ordering key

- Status: Accepted
- Date: 2026-10-16

## Context

The LRS needs one key that orders statements by `stored`, serves as the paging cursor, and bounds `since` and
`until` queries. `Guid.CreateVersion7()` is random within the same millisecond, so it is not monotonic.

## Decision

- The application generates a monotonic UUIDv7: within one millisecond a counter increases.
- Its timestamp part equals `stored`.
- SQLite stores it as `TEXT`, because .NET's mixed-endian GUID byte layout breaks v7 ordering in a `BLOB`.
- `since` and `until` are translated to bounds on this key.
- The `more` link is stateless: `from=<key>` plus the original filters. (Amended 2026-10-17: the key travels
  as the store's opaque continuation token, see ADR-0021.)

## Consequences

- Paging is a range scan on an index and needs no server-side cursor state.
- Statements stored in the same millisecond still have a defined order.
