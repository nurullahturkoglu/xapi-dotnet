# ADR-0011: Storage

- Status: Accepted
- Date: 2026-10-16

## Context

The SQL LRS schema is proven in production and under the conformance suite. Two database providers are
needed (SQLite and PostgreSQL), and schema changes need migrations.

## Options

1. EF Core.
2. Dapper with hand-written SQL.
3. Both.

## Decision

EF Core 10, with migrations in each provider package. On hot paths (statement queries) `FromSql` may be used
where benchmarks show a need.

- The statement payload is stored once, as received, in a `TEXT` JSON column; filters use narrow link tables
  (actor, activity, statement reference). PostgreSQL `JSONB` is not used in v1.
- No database ENUM or `VARCHAR(n)` columns: usage codes are `smallint`, strings are `TEXT`.
- Every filter index ends with the ordering key (ADR-0020), so paging is a range scan.
- Attachments are content-addressed by SHA-256.
- SQLite runs in WAL mode with a busy timeout; writes use `BEGIN IMMEDIATE`; GUIDs are stored as `TEXT`.

## Consequences

- Storage interfaces are public but marked `[Experimental("XAPI0001")]`, and stay experimental past 1.0
  until each is reviewed and declared stable. The provider packages depend on the exact `Xapi.Lrs` version
  so that a change to them cannot break an installed provider. (Amended 2026-10-17; the first version
  declared them stable in 1.0.) ADR-0021 lists what changes before each is declared stable.
- Changing the payload column type later requires an explicit migration (SQL LRS issue #476 shows the cost
  of doing it at startup on a large table).
