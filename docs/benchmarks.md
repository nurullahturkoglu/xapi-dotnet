# Benchmarks

`benchmarks/Xapi.Benchmarks` measures statement parsing, writing, validation and equivalence, outbox
enqueueing, and the LRS statement endpoints on SQLite and PostgreSQL, with BenchmarkDotNet. The numbers
below come from one run on one laptop. Read them as orders of magnitude and as relative costs within a
table, not as a promise for your hardware. They say nothing about other xAPI libraries or LRS products;
none were measured.

## Machine and run

| | |
| --- | --- |
| Date | 17 October 2026, 03:02 to 03:09 (UTC+3); the `Xapi.Core` table was measured again at 03:34 to 03:37 |
| Machine | MacBook Pro, Apple M1 Pro (8 performance and 2 efficiency cores), 16 GB memory, on AC power |
| OS | macOS 27.0.1 (26A434) |
| .NET | SDK 10.0.402, runtime 10.0.13, Arm64 RyuJIT, Release build |
| BenchmarkDotNet | 0.15.8, default out-of-process toolchain |
| Job | 1 launch, 3 warmup iterations, 5 measured iterations (`Short` below) |
| PostgreSQL | 17.11 (`postgres:17.11-alpine`, arm64) in Docker Desktop 28.5.1, Linux VM with 10 CPUs and 7.65 GiB |
| Source | the commit that added this file; for the `Xapi.Core` table, the commit that reduced the reader's allocations |

The machine was not idle: other builds, test runs and containers were running, and the load average was
between 3 and 12 during the run. That shows up as wide error bars on the LRS rows. The two single-writer
POST rows, which measure the same request in two benchmark classes, differ by about 60% for this reason.

## Statement JSON (`Xapi.Core`)

A statement here is about 1 KB: a named agent, a verb with a display, an activity with a definition, a
scored result and a context with a registration, a parent and a category. The batch is 100 such statements
with different ids. Each category has a plain System.Text.Json baseline over the same bytes: the Ratio column
is the cost of building the xAPI model (and, in strict mode, validating it) relative to only tokenizing the
JSON (`JsonDocument.Parse`) or only writing it (`JsonElement.WriteTo`).

| Operation | Statements | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: | ---: |
| `JsonDocument.Parse` (baseline) | 1 | 1.37 μs | 1.00 | 72 B |
| `XapiJson.ParseStatement`, strict | 1 | 5.48 μs | 4.01 | 5.2 KB |
| `XapiJson.ParseStatement`, lenient | 1 | 5.38 μs | 3.94 | 4.9 KB |
| `JsonDocument.Parse` (baseline) | 100 | 136 μs | 1.00 | 72 B |
| `XapiJson.ParseStatements`, strict | 100 | 537 μs | 3.96 | 343 KB |
| `XapiJson.ParseStatements`, lenient | 100 | 522 μs | 3.85 | 331 KB |
| `JsonElement.WriteTo` (baseline) | 1 | 0.96 μs | 1.00 | 0 B |
| `XapiJson.WriteStatement`, exact | 1 | 1.04 μs | 1.09 | 400 B |
| `JsonElement.WriteTo` (baseline) | 100 | 96 μs | 1.00 | 0 B |
| `XapiJson.WriteStatements`, exact | 100 | 111 μs | 1.16 | 35.2 KB |
| `StatementValidator.Validate` | 1 | 0.11 μs | | 336 B |
| `StatementValidator.ValidateBatch` | 100 | 11.6 μs | | 11.6 KB |
| `StatementEquivalence.AreEquivalent` | 1 | 0.09 μs | | 0 B |
| `StatementEquivalence.AreEquivalent`, each pair | 100 | 12.3 μs | | 0 B |

Reading a statement costs about four times tokenizing it, almost all of it in building the model: strict
mode, which adds validation, is only 2 to 3% slower than lenient mode. Writing is close to copying the JSON.
What the reader still allocates, about 5 KB per 1 KB statement, is mostly the model itself: the strings,
IRIs, language maps and objects a `Statement` holds.

The first version of this table had the reader at 8.85 μs and 19.7 KB per statement, 938 μs and 2.0 MB per
batch, and the validator at 1.7 KB per statement. Most of that went into JSON paths built as strings for
every property, a hash set per object for duplicate detection, and property names turned into strings to
look them up. Paths are now kept as segments and only formatted when an error is reported, known property
names are matched on their UTF-8 bytes, and duplicate checks use a bit mask or a few direct comparisons.
Error codes, paths and messages are unchanged.

## Outbox enqueue (`Xapi.Client.Outbox`)

`StatementOutbox.EnqueueAsync` of one statement without an id, so the outbox assigns one. Nothing is sent.

| Store | Mean | Allocated |
| --- | ---: | ---: |
| `InMemoryOutboxStore` | 1.28 μs | 2.1 KB |
| `FileOutboxStore` | 4.70 ms | 10.6 KB |

`FileOutboxStore` flushes its journal to disk before `EnqueueAsync` returns, so the file row is the disk's
flush latency. On macOS the .NET runtime implements `FileStream.Flush(true)` with `fcntl(F_FULLFSYNC)`, which
asks the SSD to empty its write cache. A small separate program on this machine measured 6.5 ms per 1 KB
append for `Flush(true)`, 5.0 ms for `F_FULLFSYNC` and 52 μs for a plain `fsync`. Linux and Windows flush with
`fsync` and `FlushFileBuffers`, whose cost depends on the disk; they were not measured. Enqueues are serialized,
so one outbox stores about 200 statements per second on this laptop; batch work that produces statements
faster than that should not await each enqueue on a hot path.

## LRS statement endpoints (`Xapi.Lrs`)

The LRS runs in-process on ASP.NET Core `TestServer`: requests go through the whole middleware and endpoint
pipeline, Basic authentication and the database, but not through sockets. Each benchmark process gets a new
SQLite file (WAL mode) or a new PostgreSQL database. Building the request JSON is part of the measured time
(a few microseconds per statement).

| Request | SQLite | PostgreSQL |
| --- | ---: | ---: |
| POST one statement | 2.3 ms | 4.4 ms |
| POST a batch of 100 statements | 31 ms | 50 ms |

The write rows store fresh statements throughout, so the database grows to tens of thousands of statements
during the run.

A batch of 100 costs about 0.3 to 0.5 ms per statement, against 2 to 4 ms for a statement on its own, so
senders that can batch (the outbox does, 50 per request by default) should.

### Statement queries

GET requests against 10,000 or 100,000 stored statements (plus one) spread over 10 verbs and 500 learners.
Responses use the `exact` format, which returns the stored JSON without parsing it. These rows come from a
later run than the rest of this file, on the same machine, on 17 October 2026 from 03:58 to 04:08 (UTC+3),
with other builds and tests running.

| Request | Stored | SQLite | PostgreSQL |
| --- | ---: | ---: | ---: |
| GET one statement by `statementId` | 10,000 | 0.12 ms | 0.43 ms |
| GET `verb=...&limit=100`, first page of 1,000 matches | 10,000 | 1.1 ms | 1.9 ms |
| GET the `more` link of that page | 10,000 | 1.1 ms | 2.5 ms |
| GET `verb=...&limit=100` for a verb no statement has | 10,000 | 0.29 ms | 0.82 ms |
| GET `agent=...&related_agents=true&limit=100`, 20 matches | 10,000 | 1.1 ms | 3.7 ms |
| GET one statement by `statementId` | 100,000 | 0.12 ms | 0.42 ms |
| GET `verb=...&limit=100`, first page of 10,000 matches | 100,000 | 0.96 ms | 1.9 ms |
| GET the `more` link of that page | 100,000 | 1.0 ms | 2.1 ms |
| GET `verb=...&limit=100` for a verb no statement has | 100,000 | 0.22 ms | 0.84 ms |
| GET `agent=...&related_agents=true&limit=100`, first page of 200 matches | 100,000 | 2.3 ms | 5.9 ms |

Each filter reads its own index: the verb filter the statements' verb index, `agent` and `activity` the
index of the statements that mention them (one range per place in the statement that the filter covers,
such as actor, object or context), all newest first and only as far as one page. So the time follows the
page size and not the number of statements stored. The matches through StatementRefs are a separate range
that cannot stop after a page; see [below](#statement-queries-with-statementrefs).

Until it read these ranges, the query walked the statements newest first and tested each one until it
filled the page, so the rows with few matches read the whole table. In a run of the same benchmarks just before this one, the
verb with no statements took 70 ms on SQLite and 52 ms on PostgreSQL at 100,000 statements, and the
`related_agents` query 581 ms and 15 ms.

### Statement queries with StatementRefs

The same requests with every tenth statement replaced by a StatementRef statement: an instructor's comment
whose object is a StatementRef to the statement before it (`StatementRefPercent = 10`). The other statements
and the matches of each filter are the same as above; no filter here matches through a StatementRef. Run on
17 October 2026 from 06:23 to 06:45 (UTC+3); the "before" column is the previous version of the store, from a
run at 06:13 to 06:23 with the same benchmarks at 100,000 statements. The rows without StatementRefs in the
06:23 run were within noise of the table above (at 100,000 statements on SQLite: 0.88, 0.91, 0.20 and 2.0 ms).

| Request | Stored | SQLite | SQLite before | PostgreSQL | PostgreSQL before |
| --- | ---: | ---: | ---: | ---: | ---: |
| GET one statement by `statementId` | 10,000 | 0.17 ms | | 0.43 ms | |
| GET `verb=...&limit=100`, first page of 1,000 matches | 10,000 | 2.9 ms | | 2.6 ms | |
| GET the `more` link of that page | 10,000 | 2.6 ms | | 3.1 ms | |
| GET `verb=...&limit=100` for a verb no statement has | 10,000 | 0.26 ms | | 0.80 ms | |
| GET `agent=...&related_agents=true&limit=100`, 20 matches | 10,000 | 2.0 ms | | 4.8 ms | |
| GET one statement by `statementId` | 100,000 | 0.12 ms | 0.12 ms | 0.41 ms | 0.41 ms |
| GET `verb=...&limit=100`, first page of 10,000 matches | 100,000 | 25 ms | 24 ms | 17 ms | 17 ms |
| GET the `more` link of that page | 100,000 | 25 ms | 24 ms | 11 ms | 17 ms |
| GET `verb=...&limit=100` for a verb no statement has | 100,000 | 0.42 ms | 23 ms | 0.82 ms | 0.92 ms |
| GET `agent=...&related_agents=true&limit=100`, first page of 200 matches | 100,000 | 4.1 ms | 65 ms | 5.7 ms | 6.6 ms |

The matches through StatementRefs are the one range that cannot stop after a page: the statements a
StatementRef targets are not in the order of the statements that target them. The range can start from
either side. From the `xapi_statement_ref` rows, it reads each one and looks up its target; from the filter,
it reads the statements that match from their index and looks up the rows that reach each one
(`ix_xapi_statement_ref_descendant_id`). Either costs about one lookup per row it starts from, so the smaller
side should go first.

PostgreSQL chooses from its statistics. With `EXPLAIN ANALYZE` on this data it started from the filter for
the verb with no statements (6 buffers, 0.16 ms) and for the `related_agents` query (200 lookups in
`ix_xapi_statement_ref_descendant_id`), and its SQL is unchanged; the `more` row differing between the two
runs is noise. SQLite keeps no statistics unless `ANALYZE` has run, and its plan always started from the rows:
`SCAN` of `xapi_statement_ref` with a `ux_xapi_statement_statement_id` lookup per row, 10,000 lookups for
every filtered query whatever it matched. That is the 23 ms and 65 ms in the "before" column. On SQLite the
store now counts the rows within the query's bounds (up to 10,000, about 0.3 ms) and then the filter's
matches up to that number, and starts from the smaller side; when either count is 0 the range is left out.

Where both sides are large the counts cannot help: the verb that matches 10,000 statements, with 10,000
StatementRefs, costs 10,000 lookups from either side, 25 ms on SQLite and 17 ms on PostgreSQL. Those lookups
read the statement rows, payload included: in the `sqlite3` shell on this database, counting the verb's
10,000 index entries took 0.5 ms and reading their statement ids 15 ms. An index that covers the statement
id, key, verb and registration would make the lookups index-only but would not change how the time grows;
only StatementRef matches stored at write time would make it follow the page size. Neither was added, so
this case still costs about 2.5 μs per StatementRef within the query's time bounds on SQLite. A tenth of all
statements is a deliberately high share of StatementRefs.

To decide whether StatementRef matches should be stored at write time before 1.0, the same queries also run
with every hundredth statement a StatementRef (`StatementRefPercent = 1`). At 100,000 statements, run on
17 October 2026 from 11:52 to 12:27 (UTC+3) with the load average between 7 and 13:

| Request | StatementRefs | SQLite | PostgreSQL |
| --- | ---: | ---: | ---: |
| GET `verb=...&limit=100`, first page of 10,000 matches | 0 | 0.97 ms | 2.0 ms |
| | 1,000 (1%) | 4.4 ms | 4.0 ms |
| | 10,000 (10%) | 29 ms | 20 ms |
| GET the `more` link of that page | 0 | 0.99 ms | 1.9 ms |
| | 1,000 (1%) | 4.2 ms | 4.8 ms |
| | 10,000 (10%) | 30 ms | 21 ms |
| GET `verb=...&limit=100` for a verb no statement has | 0 | 0.26 ms | 1.0 ms |
| | 1,000 (1%) | 0.26 ms | 1.3 ms |
| | 10,000 (10%) | 0.47 ms | 1.0 ms |
| GET `agent=...&related_agents=true&limit=100`, first page of 200 matches | 0 | 2.5 ms | 4.7 ms |
| | 1,000 (1%) | 3.5 ms | 5.6 ms |
| | 10,000 (10%) | 4.3 ms | 5.8 ms |

A run of the previous version of the store just before it gave the same picture (at 1%, 3.7 ms on SQLite for
the first verb page). Storing the matches at write time would remove the difference to the 0 rows: about 3 ms
per page at 1% and 20 to 30 ms at 10%, only for a filter that matches many statements while many StatementRefs
lie within the query's bounds. It was not done before 1.0; [ADR-0021](adr/0021-storage-api-before-stable.md)
gives the reasons and the design. The rows in this run also allocate about 210 KB (17%) less per page of 100
statements than the run before, because the statement JSON no longer passes through strings; the times did not
change beyond the noise.

### Concurrent writers

512 single-statement POSTs per operation, made by 1, 4 or 8 writers at once. The statements have distinct
ids but share their activities (40 lessons, one course, one category) and 500 learners, as course traffic
does, so concurrent transactions read and write the same activity and agent rows. Times are per statement.

| Database | Writers | Mean per statement | Statements per second | Store retries per 1,000 statements |
| --- | ---: | ---: | ---: | ---: |
| SQLite | 1 | 1.43 ms | about 700 | 0 |
| SQLite | 4 | 1.49 ms | about 670 | 0 |
| SQLite | 8 | 1.57 ms | about 640 | 0 |
| PostgreSQL | 1 | 3.11 ms | about 320 | 0 |
| PostgreSQL | 4 | 1.51 ms | about 660 | 60 |
| PostgreSQL | 8 | 1.09 ms | about 920 | 112 |

SQLite writes one transaction at a time (`BEGIN IMMEDIATE`), so more writers queue on the lock and the total
stays flat; there were no retries. PostgreSQL runs statement writes in `SERIALIZABLE` transactions. With 4
and 8 writers the store retried 0.06 and 0.11 times per statement, waiting 10 to 30 ms before a first retry. In a separate
run that recorded the cause, every retry was a serialization failure (SQLSTATE 40001, "could not serialize
access due to read/write dependencies among transactions"). Every request still succeeded, and throughput
with 8 writers was about three times that of one writer.

The retries do cost something. In a one-off comparison that is not part of the suite (it needs a storage
provider that is not shipped), the same 8-writer load at `READ COMMITTED` instead of `SERIALIZABLE` stored
1.4 to 1.9 times as many statements per second (two rounds of 1,600 statements each), with no retries.
`READ COMMITTED` is not a safe setting for this write path (the comment in `PostgreSqlDatabaseProvider` names
the write skews it would let through), so this is a measure of the price of correctness, not a proposal.

## Running the benchmarks

```bash
dotnet run -c Release --project benchmarks/Xapi.Benchmarks -- --filter '*'
```

Results go to `BenchmarkDotNet.Artifacts/results` in the current directory, one Markdown, CSV and HTML report
per class, plus `lrs-retries.md` with the retry counts. Useful variations:

- `--filter '*StatementJson*'`, `'*Enqueue*'`, `'*Lrs*'` or `'*ConcurrentWriter*'` runs one group, and
  `'*StatementQuery*'` only the query rows.
- `--list flat` lists the benchmarks.
- Without `--job`, every benchmark uses the short job above, and the whole suite takes about 40 minutes
  with PostgreSQL, 33 of them for the query rows, which store 10,000 or 100,000 statements per database,
  with 0, 1 or 10% StatementRefs, before each one.
  `--job Default` gives BenchmarkDotNet's full statistics (more launches and iterations until the results
  are stable) and takes much longer; use it, on an idle machine, before quoting a number.
- `XAPI_BENCH_POSTGRES=1` adds PostgreSQL to the LRS benchmarks. It starts the same image as the
  conformance job with Testcontainers, so Docker must be running. `XAPI_BENCH_POSTGRES_CONNECTION` points
  them at a server of your own instead; each benchmark process creates its own database there.

CI builds the benchmarks project with the rest of the solution but does not run it: shared CI runners are
too noisy for the numbers to mean anything.
