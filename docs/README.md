# Documentation

The guides below are planned and will be written as the packages they describe are built.

| Guide | Covers |
| --- | --- |
| `getting-started.md` | Installing the packages and sending a first statement |
| `client.md` | `XapiClient`: resources, version negotiation, documents and ETags, attachments, resilience |
| [`outbox.md`](outbox.md) | `StatementOutbox`: stores, error classification, dead letters, hosting |
| `lrs.md` | Embedding the LRS: `AddXapiLrs`, `MapXapiLrs`, storage providers, authentication, telemetry |
| `versions.md` | Differences between xAPI 1.0.3 and 2.0.0 and how the packages handle them |
| `migrating-from-tincan.md` | Mapping TinCan.NET types and calls to this library |
| `conformance.md` | Conformance suite results and the known suite issues |

## Architecture decision records

| ADR | Decision |
| --- | --- |
| [0001](adr/0001-product-shape.md) | Product shape |
| [0002](adr/0002-target-frameworks.md) | Target frameworks |
| [0003](adr/0003-serialization.md) | Serialization |
| [0004](adr/0004-model-shape.md) | Model shape |
| [0005](adr/0005-version-profiles.md) | xAPI version profiles |
| [0006](adr/0006-validation-and-errors.md) | Validation and error model |
| [0007](adr/0007-time-and-duration.md) | Time and duration |
| [0008](adr/0008-http-client.md) | HTTP client |
| [0009](adr/0009-outbox.md) | Outbox |
| [0010](adr/0010-lrs-hosting.md) | LRS hosting |
| [0011](adr/0011-storage.md) | Storage |
| [0012](adr/0012-document-concurrency.md) | Document concurrency |
| [0013](adr/0013-authentication.md) | Authentication |
| [0014](adr/0014-observability.md) | Observability |
| [0015](adr/0015-conformance-gate.md) | Conformance gate |
| [0016](adr/0016-versioning-and-release.md) | Versioning and release |
| [0017](adr/0017-public-api-tracking.md) | Public API tracking |
| [0018](adr/0018-license-and-attribution.md) | License and attribution |
| [0019](adr/0019-test-infrastructure.md) | Test infrastructure |
| [0020](adr/0020-statement-ordering-key.md) | Statement ordering key |

A new decision gets the next number. A decision that is replaced keeps its file, with its status changed to "Superseded by ADR-NNNN".
