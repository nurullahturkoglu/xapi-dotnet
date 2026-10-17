# Outbox

`Xapi.Client.Outbox` puts a durable queue in front of `IXapiClient`. `EnqueueAsync` stores a statement on
disk and returns; the outbox delivers it later, in order and in batches, and keeps it through network
outages, LRS downtime and app restarts. It targets netstandard2.0 and net10.0 and uses no reflection, so it
runs on .NET Framework, under Native AOT and (once verified, see below) under Unity IL2CPP.

## Quick start

Without dependency injection (desktop apps, Unity, console tools):

```csharp
var http = new HttpClient();
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Basic(key, secret),
});
var store = new FileOutboxStore(Path.Combine(dataDirectory, "xapi-outbox"));
var outbox = new StatementOutbox(client, store);

// Deliver in the background until the token is cancelled or the outbox is disposed
_ = outbox.RunAsync(shutdown.Token);

await outbox.EnqueueAsync(statement);

// Or deliver now, for example before the app is suspended
var result = await outbox.FlushAsync();
```

With a .NET host (net10.0), `AddXapiOutbox` registers `StatementOutbox` as a singleton and runs its loop as a
hosted service. The outbox resolves `IXapiClient` again for every flush, so `IHttpClientFactory` handler
rotation keeps working:

```csharp
builder.Services.AddXapiClient(o =>
{
    o.Endpoint = new Uri("https://lrs.example.com/xapi/");
    o.Credentials = XapiCredentials.Basic(key, secret);
});
builder.Services.AddXapiOutbox("data/xapi-outbox", o => o.BatchSize = 100);
```

## Delivery guarantees

Delivery is **at least once**. A statement leaves the store only after the LRS has answered for it, so a
crash, a timeout or a lost response means it is sent again.

`EnqueueAsync` gives every statement an id (a UUID version 7) before it is stored, unless it already has
one. A statement sent twice therefore reaches the LRS with the same id both times, and a conforming LRS keeps
the first copy and accepts or answers 409 to the second (IEEE 9274.1.1, 4.1.6.1). With such an LRS the result
is **exactly once**. The outbox treats a 409 for a statement as delivered when the LRS's stored statement is
equivalent under the standard's immutability rules (`StatementEquivalence`).

Statements are delivered in the order they were enqueued, except that a dead-lettered statement is skipped
and a requeued dead letter goes to the end of the queue. One flush runs at a time; enqueueing during a flush
is safe.

`EnqueueAsync` also sets a missing `timestamp` to the enqueue time, so a statement delivered days later still
says when it happened, and validates the statement with the xAPI 2.0.0 rules (a superset of 1.0.3).

## How responses are handled

| What happened | What the outbox does |
| --- | --- |
| 200, 204 | Delivered: removed from the store, `Delivered` event. |
| 200 with ids missing for statements the LRS already had (lrsql does this) | Delivered; the client returns the ids it sent. |
| 400 or 422 for a batch | The batch is split in halves, recursively, until the invalid statements are found; about 2·log2(n) extra requests per invalid statement. The valid ones are delivered in order. |
| 400 or 422 for one statement | Dead letter (`Rejected`), with the status code and the LRS's response body. |
| `XapiValidationException` from the client before sending (for example a 2.0-only property and a 1.0.3 LRS) | Treated like 400, without a request. |
| 409 for one statement | The stored statement is fetched (`statementId`, then `voidedStatementId`) and compared with `StatementEquivalence`. Equivalent: delivered. Different, missing, or the fetch fails permanently: dead letter (`Conflict`). |
| 409 for a batch | The response does not say which statement conflicted, so the batch is sent again one statement at a time, and each 409 is resolved as above. |
| 413 for a batch | The batch is split in halves, and the batch size is lowered for the rest of the outbox's life. |
| 413 for one statement | Dead letter (`TooLarge`). |
| 408, 429, 5xx (except 501 and 505), network error, timeout | Transient: the statements stay and the attempt is counted. The outbox waits a random delay between half and all of `InitialRetryDelay × 2^(n−1)` (at most `MaxRetryDelay`), or the LRS's `Retry-After` if that is longer. Nothing else is sent until then. |
| A 2xx response the client cannot read (for example a proxy's HTML page) | Transient. Sending again is safe because the ids are fixed. |
| Any other exception (a failing token provider, a handler bug) | Transient. Keeping the statements is safer than dead-lettering a healthy queue. |
| 401, 403 | Paused: nothing is sent and nothing is dead-lettered until `Resume()` is called or a new outbox is created. `Paused` event; the status shows the error. |
| Other 4xx and 3xx, 501, 505, no common xAPI version | Paused, as above. These mean the endpoint or configuration is wrong, and dead-lettering would empty the queue one statement at a time. |
| Transient failures reach `MaxAttempts` (off by default) | Dead letter (`MaxAttemptsExceeded`). |

The background loop (`RunAsync`) sends right away, whenever a statement is enqueued, and at the retry time
after a transient failure. It never ends on an error; store failures are handled like transient failures.
`FlushAsync` sends immediately, ignoring the retry delay, but not while the outbox is paused.

`GetStatusAsync` returns the state (`Idle`, `Sending`, `WaitingToRetry`, `Paused`), the pending and
dead-letter counts, the last error and the retry time. The events `Delivered`, `DeadLettered` and `Paused`
are raised on the thread that runs the flush; exceptions thrown by handlers are caught and ignored.

## Dead letters

Dead letters stay in the store with the statement, the reason, the status code, the response body (cut at
16 KiB), the enqueue time and the number of attempts. `GetDeadLettersAsync` lists them,
`RequeueDeadLetterAsync` and `RequeueAllDeadLettersAsync` move them back to the end of the queue with no
attempts, and `RemoveDeadLetterAsync` deletes one. They do not count towards `MaxQueueSize`.

## Metrics

The outbox reports on the client's `Xapi.Client` meter (`XapiClientDiagnostics.MeterName`), next to
`xapi.client.statements.sent`, which counts what it delivers. The names are constants of `OutboxDiagnostics`.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `xapi.client.outbox.statements.retried` | Counter | `{statement}` | `error.type` |
| `xapi.client.outbox.statements.dead_lettered` | Counter | `{statement}` | `xapi.outbox.dead_letter.reason` |
| `xapi.client.outbox.statements.pending` | Observable up-down counter | `{statement}` | none |

`retried` counts the statements of a send that failed with a transient error and stay queued for the next
attempt; `error.type` is the LRS's status code (`429`, `503`) or the exception's full type name
(`System.Net.Http.HttpRequestException`). A statement that reaches `MaxAttempts` is counted as dead-lettered
instead. `xapi.outbox.dead_letter.reason` is `rejected`, `too_large`, `conflict` or `max_attempts_exceeded`.

`pending` is the number of statements waiting to be delivered, dead letters not included, summed over the live
outboxes on the meter. It is an up-down counter rather than a gauge because the counts of several outboxes add
up. It is read from the store when the metrics are collected; a custom store whose `GetPendingCountAsync` does
not complete synchronously counts as 0. An outbox from `AddXapiOutbox` gets its meter from the host's
`IMeterFactory`; one created with `new StatementOutbox(...)` reports on a meter shared by the process until it
is disposed.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `BatchSize` | 50 | Most statements per request. |
| `MaxQueueSize` | 100,000 | Most pending statements. When reached, `EnqueueAsync` throws `OutboxFullException` and stores nothing; nothing queued is dropped. `null` for no limit. |
| `InitialRetryDelay` | 2 s | Backoff after the first transient failure. |
| `MaxRetryDelay` | 5 min | Longest backoff (a longer `Retry-After` still wins). |
| `MaxAttempts` | `null` | Transient failures before a statement is dead-lettered; `null` retries for ever, which suits offline use. |
| `TimeProvider` | `TimeProvider.System` | Clock and timers, replaceable in tests. |

## Attachments

Attachment data is not supported in this version: `EnqueueAsync` rejects a statement with an Attachment that
has no `fileUrl` (error code `attachment.payload.missing`). Attachments that only reference a `fileUrl` are
fine. Storing binary payloads in the journal would make it large and slow to read, and bisection would have
to keep each payload with its statement. Send statements with attachment data, including signed statements,
directly with `IXapiClient.SaveStatementAsync`.

## Storage

`IOutboxStore` is the storage abstraction. Two stores are built in:

- `FileOutboxStore(directory)` for applications.
- `InMemoryOutboxStore` for tests, or where losing queued statements on exit is acceptable.

### File format

The directory holds:

- `outbox.v1.jsonl`, the journal. Each line is one change, written as
  `{"crc":"<CRC-32 in hex>","record":{...}}` and a newline. The CRC covers the exact UTF-8 bytes of the record
  object. Records are `add` (a statement, its enqueue time, attempts and last error), `remove` (delivered
  ids), `attempt` (ids and error of a failed send), `dead` (a complete dead letter), `requeue` and
  `removeDead`. Statements are stored as xAPI 2.0.0 JSON.
- `outbox.lock`, held open with exclusive sharing while the store is open, so a second store in this or
  another process fails with an `IOException` instead of corrupting the journal.
- Rarely, `outbox.v1.jsonl.tmp` (an interrupted compaction, deleted on open) and
  `outbox.v1.jsonl.corrupt-<time>` (a copy of a damaged journal, see below).

The store reads the journal once, in its constructor, and keeps the queue in memory. It suits queues of up
to tens of thousands of statements.

### Crash safety

Every change is appended to the journal and flushed to disk (`FileStream.Flush(true)`, which calls `fsync`)
before the method returns, and the in-memory state changes only after that. So anything the outbox reported
as stored is on disk.

If the process dies in the middle of an append, the journal ends with a line that has no newline. On the next
open that tail is cut off and reported in `FileOutboxStore.Recovery` (`DiscardedTailBytes`). It held the one
change that was never acknowledged, so nothing reported as stored is lost; a test cuts the journal at every
byte of its last record to check this. If a write fails without killing the process, the store truncates the
journal back to its last complete record before the exception is thrown.

When the journal is more than twice the size of the live state (and over 256 KiB), it is compacted: the live
state is written to `outbox.v1.jsonl.tmp`, flushed, and renamed over the journal with `File.Replace`. A crash
before the rename leaves the old journal, which is complete; a crash after it leaves the new one.

A line whose checksum does not match (the file was damaged on disk, not by a crash) is skipped, counted in
`Recovery.UnreadableRecords`, the damaged file is copied to `outbox.v1.jsonl.corrupt-<time>`, and the journal
is rewritten without it. A skipped `add` loses that statement; a skipped `remove` makes the outbox send
statements again, which is harmless because their ids are fixed.

Power loss is different from a process crash: then durability also depends on the file system and the
disk. .NET cannot `fsync` a directory, so on some file systems a compaction rename made just before power
loss may be lost; the old journal is then used, and changes acknowledged after that compaction may be lost
with it. Journaling file systems (ext4, APFS, NTFS) normally persist the rename.

### Custom stores

A custom store implements the ten methods of `IOutboxStore`. It must be thread-safe, keep pending entries in
order and keyed by statement id, and make every change durable before its task completes. The tests in
`tests/Xapi.Client.Outbox.Tests/Stores/StoreContractTests.cs` describe the contract.

## Unity

Unity support is not verified yet; it is planned for milestone M8. The package is built with that in mind:

- No reflection, no dynamic code and no Microsoft.Extensions dependencies outside the net10.0 build. JSON goes
  through the hand-written reader and writer of `Xapi.Core`.
- Put the store under `Application.persistentDataPath`, the one location that survives app updates on
  every platform.
- Start the loop once (`_ = outbox.RunAsync(token)`) and call `FlushAsync` from `OnApplicationPause(true)`,
  because mobile platforms may kill a suspended app without warning. Dispose the outbox and the store in
  `OnApplicationQuit`.
- Events are usually raised on a thread-pool thread. Marshal to the main thread before touching Unity objects.
- WebGL has no threads or synchronous file flush; whether `HttpClient` and the file store work there is part
  of the M8 spike.
