# xAPI versions

The packages support two versions of the standard: xAPI 1.0.3 (ADL, 2016) and xAPI 2.0.0, published as IEEE
9274.1.1-2023. The data model and the protocol changed little between them, but enough that an LRS must
reject, store or answer some requests differently, so the version is never implicit.

## The version is always explicit

`XapiVersion` has two values, `XapiVersion.V1_0_3` and `XapiVersion.V2_0_0`. Reading, writing and validating
JSON in `Xapi.Core` take one as a parameter. There is no global default, and one process can handle both
versions, which the LRS needs.

`XapiVersion.TryParseHeader` maps the `X-Experience-API-Version` header: `1.0` and `1.0.x` map to 1.0.3, `2.0`
and `2.0.x` to 2.0.0. Anything else, including `0.95`, `1.1.0` and `2.1.0`, is not a supported version.

## Differences that affect your code

| | 1.0.3 | 2.0.0 |
| --- | --- | --- |
| `context.contextAgents`, `context.contextGroups` | Do not exist. Strict reading reports `json.property.version`, validation `context.version`, and writing throws `InvalidOperationException`. | Supported. `relevantTypes`, when present, must hold at least one IRI. |
| `context.instructor`, `context.team` | Supported. | Still supported, but deprecated in favour of `contextAgents` and `contextGroups`. |
| Statement `version` property | Must be `1.0.x`. | Must be `1.0.x` or `2.0.0`. |
| Timestamps | `T` between date and time. | `T` or a space. |

Both versions require a UTC offset on timestamps and reject `-00:00`. The library writes timestamps in UTC
with a `Z` and always three (or seven) fraction digits.

1.0.3 compares durations as text, and 2.0 ignores precision beyond 0.01 second. `XapiDuration` compares
component by component and ignores precision beyond 0.01 second in both versions, so `PT1.001S` equals `PT1S`
and `PT60S` does not equal `PT1M`.

Two rules are applied to both versions although only 1.0.3 has them, because the conformance suite tests
them in the 2.0.0 battery too: `context.revision` and `context.platform` are only allowed when the object is
an Activity. See [conformance.md](conformance.md#where-the-suite-and-the-standard-disagree).

## Differences in LRS behaviour

These are handled by `Xapi.Lrs` according to the version of the request, and matter to clients that talk to
any LRS.

| | 1.0.3 | 2.0.0 |
| --- | --- | --- |
| Statement without `version` | The LRS stores `1.0.0`. | The LRS stores `2.0.0`. |
| State PUT without `If-Match` or `If-None-Match` | Allowed; replaces the document. | 409 when the document exists. |
| Activity or Agent Profile PUT without a precondition | 409 when the document exists, 400 when it does not. | 409 when the document exists. |
| Alternate Request Syntax (a POST with `method=` for clients that cannot send other methods or headers) | Supported. | Not part of the standard; such requests are rejected. |
| `X-Experience-API-Version` on responses | `1.0.3` | `2.0.0` |

POST and DELETE of documents never require a precondition in this LRS, in either version. Statements are
returned as they were stored, so a 1.0.3 client can receive a statement stored by a 2.0 client, with
`contextAgents` in it. `Xapi.Client` reads responses leniently and accepts that.

## What the client and the outbox do

`Xapi.Client` negotiates the version through the About resource unless `XapiClientOptions.Version` is set,
and prefers 2.0.0 (see [client.md](client.md#versions-and-negotiation)). Statements are validated against the
version in use before they are sent, so a statement with `contextAgents` fails with `XapiValidationException`
when the LRS only speaks 1.0.3, without a request. A statement that is valid in both versions works with
both.

`StatementOutbox.EnqueueAsync` validates with the 2.0.0 rules, which accept everything 1.0.3 does, because
the version of the LRS may not be known yet when a statement is queued. If the LRS turns out to speak only
1.0.3 and a queued statement uses a 2.0-only property, delivery fails with `XapiValidationException` and the
statement becomes a dead letter (`Rejected`).

## Which version to use

Use 2.0.0 when the LRS supports it; negotiation does this for you. Set 1.0.3 explicitly when you need
identical behaviour against LRSs of both versions, for example while moving from TinCan.NET, which only
speaks 1.0.3. An LRS built with `Xapi.Lrs` serves both versions unless `EnableVersion1_0_3` is turned off.
