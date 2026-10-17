---
uid: Xapi.Lrs.Storage
summary: *content
---

The storage extension points of `Xapi.Lrs`, used by the SQLite and PostgreSQL packages and by custom stores.

> [!WARNING]
> Every type in this namespace is marked `[Experimental("XAPI0001")]` and is not covered by semantic
> versioning, also after 1.0: any release can change it until it is declared stable. Using one is a compile
> error until `XAPI0001` is suppressed. See [Experimental storage API](../lrs.md#experimental-storage-api),
> and [ADR-0021](../adr/0021-storage-api-before-stable.md) for what is expected to change before each part is
> declared stable.
