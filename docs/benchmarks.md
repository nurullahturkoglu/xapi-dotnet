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
| GET one statement by `statementId` | 0.16 ms | 0.42 ms |
| GET `verb=...&limit=100`, first page of 1,000 matches | 1.6 ms | 1.9 ms |
| GET the `more` link of that page | 1.8 ms | 1.9 ms |
| GET `verb=...&limit=100` for a verb no statement has | 6.9 ms | 2.8 ms |

The write rows store fresh statements throughout, so the database grows to tens of thousands of statements
during the run. The query rows run against 10,001 statements spread over 10 verbs. Responses use the
`exact` format, which returns the stored JSON without parsing it.

A batch of 100 costs about 0.3 to 0.5 ms per statement, against 2 to 4 ms for a statement on its own, so
senders that can batch (the outbox does, 50 per request by default) should.

The last query row is the worst case for the statement query: the verb filter also matches statements whose
StatementRef target has the verb, and that `OR` keeps SQLite from using the verb index, so it walks the
statements newest first until it fills the page. When few statements match, it reads all of them. The cost
grows with the table: in a separate run against 100,000 statements on SQLite, the same query took 70 ms.

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

- `--filter '*StatementJson*'`, `'*Enqueue*'`, `'*Lrs*'` or `'*ConcurrentWriter*'` runs one group.
- `--list flat` lists the benchmarks.
- Without `--job`, every benchmark uses the short job above, and the whole suite takes about 6 minutes
  with PostgreSQL. `--job Default` gives BenchmarkDotNet's full statistics (more launches and iterations
  until the results are stable) and takes much longer; use it, on an idle machine, before quoting a number.
- `XAPI_BENCH_POSTGRES=1` adds PostgreSQL to the LRS benchmarks. It starts the same image as the
  conformance job with Testcontainers, so Docker must be running. `XAPI_BENCH_POSTGRES_CONNECTION` points
  them at a server of your own instead; each benchmark process creates its own database there.

CI builds the benchmarks project with the rest of the solution but does not run it: shared CI runners are
too noisy for the numbers to mean anything.
