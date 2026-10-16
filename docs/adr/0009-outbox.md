# ADR-0009: Outbox

- Status: Accepted
- Date: 2026-10-16

## Context

CMU SEI and Float each built a statement queue by hand, and both get error handling wrong (for example, one
400 response discards a whole batch of 50).

## Decision

A separate package, `Xapi.Client.Outbox`.

- `EnqueueAsync` assigns the statement `Id` (UUIDv7) on the client side and validates the statement.
- Statements are sent in batches. Responses are classified as follows:

| Response | Behaviour |
| --- | --- |
| 200, 204 | Sent; removed from the outbox |
| 400 for a batch | The batch is split in half until the invalid statement is isolated; it goes to dead letters, the rest continue |
| 400 for a single statement | Dead letter, with the response body |
| 401, 403 | The outbox pauses and reports the error; statements stay; nothing is dead-lettered |
| 409 | The stored statement is fetched and compared with `StatementEquivalence`; equivalent means sent, otherwise dead letter |
| 413 | The batch size is halved; a single statement that still gets 413 is dead-lettered |
| 408, 429, 5xx, network error, timeout | Exponential backoff with jitter; `Retry-After` is honoured |
| 400 rejecting the version header | Renegotiate once through `/about`, then retry |

- Stores: in memory, and file based (append-only JSON Lines with periodic compaction and `fsync`).

## Consequences

- Sending the same statement twice is idempotent at the LRS, because the ID is fixed before the first send.
- A single bad statement never blocks or discards the rest of the queue.
