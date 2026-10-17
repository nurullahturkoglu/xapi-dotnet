# ADR-0021: Storage API before it is declared stable

- Status: Accepted
- Date: 2026-10-17

## Context

ADR-0011 made the storage interfaces public but experimental (`XAPI0001`), and the
[experimental storage API](../lrs.md#experimental-storage-api) policy says that each one stays experimental,
also after 1.0, until it is declared stable on its own in the changelog. A review of that API found eight
problems, D1 to D8. Three of them were cheap to fix and are fixed (D2, D4, D8). The other five change the shape
of the interfaces or of their registration; this record lists them as the conditions for declaring each part
stable. The same review asked whether StatementRef matches should be resolved when a statement is written
instead of when a query runs; that is decided at the end.

## Decision

Fixed now:

- D2, page cursor. `StatementFilter.After` and `StatementPage.Continuation` were the `Guid` storage key of
  ADR-0020, so a store that orders statements by something else could not page. Both are now an opaque
  `string? ContinuationToken`, the name the Azure SDK uses for the same thing. The EF store writes its key as 22
  base64url characters. The `more` URL keeps its shape, with the token where the key was (this amends the
  `from=<key>` of ADR-0020). A client can change the token, so a store throws `ArgumentException` for one it
  cannot read, and the LRS answers 400 as it did when it parsed the key itself. `StatementFilter.Limit`
  keeps its default of 100: the LRS always sets it from `MaxStatementsPerPage` and the request.
- D4, statement JSON as UTF-8. `StatementRecord.Json` and `StoredStatement.Json` were strings, so a statement
  went from UTF-8 to UTF-16 and back on its way from the writer to the database and from the database to the
  response. They are now `ReadOnlyMemory<byte> Utf8Json`, and query results are written to the response
  without an intermediate string. The payload column stays `TEXT` (ADR-0011); both EF Core providers read and
  write it as a string, so the EF store converts once, at that boundary, and the schema is unchanged. A store
  on a database that holds bytes does not convert at all. In the query benchmarks a page of 100 statements
  allocates about 17% less (1,026 KB instead of 1,239 KB on SQLite); request times and writes did not change
  beyond the noise of the run.
- D8, SHA-2 casing. Hashes are matched without regard to case and kept in lower-case hex: `AttachmentContent`
  and `AttachmentSummary` lower-case what they are given, the multipart reader keys parts by the lower-cased
  `X-Experience-API-Hash`, statements' `sha2` values are lower-cased before they are matched, and the EF store
  looks data up in lower case. This already held on every path; a test now sends an upper-case part header.

Conditions before a part is declared stable:

- D1, attachments in memory. `StatementRecord.Attachments` and `GetAttachmentContentAsync` carry the whole data
  as `ReadOnlyMemory<byte>`, so a request's attachments (up to `MaxAttachmentBytes`, 50 MiB by default) are in
  memory while the batch is stored, and a response holds one attachment at a time. Before `IStatementStore` is
  declared stable, attachment data moves to a separate `IAttachmentStore` keyed by SHA-2, with Stream-based
  `SaveAsync` and `OpenReadAsync`, and the statement store only records which hashes a statement refers to.
  Data is saved before the statements that refer to it, so a stored statement never points at missing data;
  data whose statements were rejected is harmless, because it is addressed by content, and can be collected
  later. This also lets attachment data live in blob storage.
- D3, xAPI rules inside the store. `StoreAsync(statements, bool updateDefinitions)` leaves to every store the
  equivalence check behind 409, voiding in either arrival order, merging Activity and Agent definitions under
  the `define` scope, and the StatementRef ancestry that queries use. These are rules of the standard, not of
  storage, and a store that gets one wrong only shows it in the conformance suite. Before `IStatementStore` is
  declared stable, the boolean becomes a request object that can grow without breaking implementations, and
  one of two things happens: the rules move into the LRS, leaving the store primitives (read by ids, insert,
  mark voided, upsert definitions) inside one write transaction, or the documentation states that a custom
  statement store is only supported as a decorator of the built-in one (D6). The first is preferred; it needs
  the per-provider transaction rules of `XapiLrsDatabaseProvider` to move with it, so the choice is made when
  there is a store outside this repository to test it against.
- D5, EF Core on the public surface. `XapiLrsDbContext` and `XapiLrsDatabaseProvider` are public only so that
  the provider packages can ship migrations and configure EF Core, and they expose EF Core types that change
  with EF Core. They stay experimental and are not candidates for stable. Applications migrate with
  `MigrateXapiLrsDatabaseAsync` and check the database with `AddXapiLrsHealthCheck`, which expose no EF Core
  type.
- D6, registration. `UseStatementStore<TStore>` and `UseDocumentStore<TStore>` register a singleton and remove
  the built-in store, so a store cannot depend on scoped services (a scoped `DbContext`, the current tenant) and
  cannot wrap the built-in one for caching, auditing or routing. Before either is declared stable, the builder
  gets overloads that take a factory and a `ServiceLifetime`, and `Decorate<TDecorator>`-style methods whose
  decorator receives the store registered before it. The LRS then resolves stores per request; today
  `StatementWriter`, a singleton, holds the statement store.
- D7, provider seed. `XapiLrsDatabaseProvider` changes whenever the EF store changes how it writes, so it moves
  in lockstep with `Xapi.Lrs`. It stays experimental, the providers stay in this repository, and the provider
  packages keep depending on the exact version of `Xapi.Lrs` they were built with (`[x.y.z]`, already in
  place). Other databases are supported through the store interfaces, not through a provider.

Order: `IDocumentStore` is the first candidate. It has no attachment data, the LRS decides every precondition
and merge itself and hands the store one atomic `ChangeAsync` (ADR-0012), so it needs only D6. `IStatementStore`
follows once D1, D3 and D6 are done. `XapiLrsDbContext` and `XapiLrsDatabaseProvider` are not planned to become
stable.

StatementRef matches stay resolved when a query runs (4.1.6.1 "Filter Conditions for StatementRefs"). The
query benchmarks were run with 0, 1 and 10% of 100,000 statements being StatementRefs, on SQLite and
PostgreSQL ([numbers](../benchmarks.md#statement-queries-with-statementrefs)). Resolving at write time would
remove the difference to the run without StatementRefs: for the first page of a verb that 10,000 statements
have, about 3 ms at 1% on SQLite (4.4 ms against 0.97 ms) and 2 ms on PostgreSQL (4.0 against 2.0 ms), and
about 28 ms and 18 ms at 10%. Filters that match few statements gain 1 ms or less at either share. The cost
grows with the StatementRefs inside the query's time bounds, about 3 μs each on SQLite and 2 μs on
PostgreSQL, so a `since` bound limits it. This is not worth a schema change before 1.0:

- The change is internal to the EF store. Neither `IStatementStore` nor the HTTP API changes, and the schema
  is not public, so it can ship later as an additive migration (link rows for the matches through
  StatementRefs, filled from the existing rows) in both provider packages, which release in lockstep (D7).
- At a realistic share of StatementRefs the gain is a few milliseconds per page at 100,000 statements.
- It moves the work to every write: a StatementRef statement would copy the verb, registration, actor and
  activity rows of the statements it reaches, and a target that arrives after the statements referring to it
  would add rows to each of them, as `xapi_statement_ref` already does for ids.

It is a follow-up for when an LRS with millions of statements and a large share of StatementRefs shows slow
broad queries. Until then the EF store keeps choosing, on SQLite, the smaller side to start the read-time
lookup from.

## Consequences

- Stores written against the earlier shape fail to compile: `After`, `Continuation` and `Json` are gone. This
  is allowed for `XAPI0001` types and is listed under "Breaking" in the changelog.
- The paging token, the JSON shape and the hash casing are settled; what still changes before a part is
  declared stable is listed above, so implementers of a custom store know which parts of their code will move.
- Declaring `IDocumentStore` stable needs no change to the statement path.
