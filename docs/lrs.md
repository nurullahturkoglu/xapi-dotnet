# LRS

`Xapi.Lrs` adds an xAPI Learning Record Store to an ASP.NET Core app: minimal API endpoints under a path
prefix, version negotiation, Basic or delegated authentication with xAPI scopes, and EF Core storage. Add
`Xapi.Lrs.Sqlite` or `Xapi.Lrs.PostgreSql` for the database. The packages target net10.0. They cannot be
trimmed or published with Native AOT, because EF Core cannot.

## Adding the LRS

`AddXapiLrs` registers the services and the Basic authentication scheme, and returns a builder on which a
database is chosen. `MapXapiLrs(prefix)` maps the resources under `prefix` (default `/xapi`), which is the
endpoint clients use. [getting-started.md](getting-started.md#host-an-lrs) has a complete `Program.cs`, and
[samples/EmbedInAspNetCore](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/EmbedInAspNetCore) adds the LRS to an app with endpoints of its own.

| Path under the prefix | Methods |
| --- | --- |
| `/about` | GET, HEAD. No authentication, and any version header is accepted. |
| `/statements` | GET, HEAD, PUT, POST |
| `/agents` | GET, HEAD |
| `/activities` | GET, HEAD |
| `/activities/state` | GET, HEAD, PUT, POST, DELETE |
| `/activities/profile` | GET, HEAD, PUT, POST, DELETE |
| `/agents/profile` | GET, HEAD, PUT, POST, DELETE |

Other paths under the prefix answer 404, and other methods 405 with an `Allow` header. Errors are
`application/problem+json`. Every response of these endpoints carries `X-Experience-API-Version`, and the
Statement Resource also sends `X-Experience-API-Consistent-Through`.

Statements with attachment data are accepted as `multipart/mixed`. Each part is matched to its Attachment
object by SHA-2, and missing, extra or mismatching parts get 400. Attachment data is stored once per SHA-2,
and `attachments=true` on a GET returns it with the statements. Signed statements are checked as IEEE
9274.1.1, 4.2.6 requires; [signatures.md](signatures.md) describes the checks.

The LRS passes all tests of the ADL LRS Conformance Test Suite at commit `5bc232d`, for xAPI 1.0.3 and 2.0.0,
on SQLite and PostgreSQL; see [conformance.md](conformance.md).

## Options

<!-- snippet: lrs-options -->
```csharp
builder.Services
    .AddXapiLrs(options =>
    {
        options.Credentials.Add("course-player", builder.Configuration["LrsSecrets:CoursePlayer"]!,
            XapiScopes.StatementsWrite, XapiScopes.StatementsReadMine, XapiScopes.State);
        options.Credentials.Add("reporting", builder.Configuration["LrsSecrets:Reporting"]!, XapiScopes.AllRead);
        options.Credentials.Add("lms", builder.Configuration["LrsSecrets:Lms"]!,
            Agent.FromAccount(new Uri("https://lms.example.com/"), "lms"), XapiScopes.All);

        options.AuthorityHomePage = new Uri("https://lrs.example.com/");
        options.MaxDocumentBytes = 256 * 1024;
        options.MaxStatementsPerPage = 500;
        options.MigrateOnStartup = true;
    })
    .UseSqlite(builder.Configuration.GetConnectionString("Lrs")!);
```

| Option | Default | Meaning |
| --- | --- | --- |
| `Credentials` | empty | Basic credentials and their scopes; see [Authentication](#authentication). |
| `AuthenticationScheme` | `null` | An ASP.NET Core scheme to authenticate with instead of the built-in Basic credentials. |
| `ScopeClaimType` | `scope` | The claim with the xAPI scopes, for `AuthenticationScheme`. |
| `AuthorityFactory` | `null` | Maps a principal of `AuthenticationScheme` to the authority Agent. |
| `AuthorityHomePage` | `https://xapi-dotnet.invalid/credentials` | The `homePage` of generated authority accounts. Set it to a URL you control before storing statements you keep: the authority is stored with each statement and not rewritten later. |
| `EnableVersion1_0_3` | `true` | Accept `1.0.x` requests and handle them with xAPI 1.0.3 rules. When `false`, only 2.0 requests are accepted. |
| `MaxDocumentBytes` | 1 MiB | The largest State or profile document, also after a POST merge. Larger ones get 413. |
| `MaxStatementBytes` | 16 MiB | The most statement JSON in one statement PUT or POST: the whole `application/json` body, or the first part of a `multipart/mixed` one. More gets 413. |
| `MaxAttachmentBytes` | 50 MiB | The most attachment data in one statement request, all parts together. More gets 413. |
| `MaxStatementsPerBatch` | 1,000 | The most statements in one statement POST. More gets 413. |
| `MaxAlternateRequestBytes` | 1 MiB | The largest form of an xAPI 1.0.3 Alternate Request Syntax request. More gets 413. |
| `MaxStatementsPerPage` | 100 | The page size of statement queries when the request asks for `limit=0` or no limit; larger limits are lowered to it. |
| `MigrateOnStartup` | `false` | Apply database migrations when the host starts. |

The setters accept any value; the options are checked when the host starts. Startup stops with an
`OptionsValidationException` that lists every problem when there are no `Credentials` and no
`AuthenticationScheme` (no request could be authenticated), when `AuthenticationScheme` is empty or the
built-in Basic scheme `XapiBasic`, when `ScopeClaimType` is empty, when `AuthorityHomePage` is not an absolute
URI, when a size or count limit is below 1 (`MaxAttachmentBytes` below 0), or when a credential is invalid: a
key that is missing, empty, contains a colon or appears twice, a missing or empty secret, no scopes, or a scope
that is not one of the xAPI names in the table under [Basic credentials](#basic-credentials). The messages name a
credential by its index and key and never contain its secret.

### Options from configuration

The `AddXapiLrs` overload that takes a configuration section binds the options from it, credentials
included:

<!-- snippet: lrs-configuration -->
```csharp
builder.Services
    .AddXapiLrs(builder.Configuration.GetSection("Lrs"))
    .UseSqlite(builder.Configuration.GetConnectionString("Lrs")!);
```

```json
{
  "Lrs": {
    "AuthorityHomePage": "https://lrs.example.com/",
    "MaxStatementsPerPage": 500,
    "Credentials": [
      { "Key": "course-player", "Scopes": [ "statements/write", "statements/read/mine", "state" ] },
      { "Key": "reporting", "Scopes": [ "all/read" ] }
    ]
  }
}
```

Keep the secrets out of `appsettings.json` and add them from a secret store or the environment, for example
`Lrs__Credentials__0__Secret`. Keys are the option names, and a key that is not one, such as a misspelt
`MaxStatementPerPage`, stops the host at startup. `Scopes` is an array with one xAPI scope name per entry
(`Lrs__Credentials__1__Scopes__0=all/read`); the names are the constants of `XapiScopes`.

Empty values, such as an empty environment variable, are not skipped. An empty number or switch fails to
convert, with a message that names its key, and an empty `AuthorityHomePage` or text option such as
`AuthenticationScheme` is set to null or empty, which startup then rejects. Remove a value rather than set it to
empty to keep the default.

Credentials from configuration get the default authority, an account on `AuthorityHomePage` named after the
key. `AuthorityFactory` and a credential with another authority are set in code, in the callback that the
overload takes as its second argument and runs after binding the section, for example
`options.Credentials.Add("lms", secret, authority, XapiScopes.All)`. Credentials added there join those from
configuration, and a key that appears twice fails at startup. The section can also be bound with
`builder.Services.Configure<XapiLrsOptions>(section)` before or after `AddXapiLrs`, with the reflection-based or
the source-generated configuration binder; that binds the same way but ignores keys it does not know.

## Storage

| Package | Builder method | Notes |
| --- | --- | --- |
| `Xapi.Lrs.Sqlite` | `UseSqlite(connectionString)` | WAL mode and a 30 second busy timeout. Statement writes take the write lock when their transaction begins (`BEGIN IMMEDIATE`), so concurrent writers wait for each other instead of failing. Suits one server. |
| `Xapi.Lrs.PostgreSql` | `UsePostgreSql(connectionString)` | For several app instances sharing one database. PostgreSQL cannot index values over about 2.7 KB, so a statement or document request with a longer IRI, agent identifier or document id gets 400. |

<!-- snippet: lrs-postgresql -->
```csharp
builder.Services
    .AddXapiLrs(builder.Configuration.GetSection("Lrs"))
    .UsePostgreSql(builder.Configuration.GetConnectionString("Lrs")!);
```

Each statement is stored once, as the JSON that was received, with separate tables for the agents,
activities and statement references the query filters use. Attachment data is stored once per SHA-2, and
documents with their SHA-1 ETag. The
schema is the same on both databases.

### Migrations

Each provider package carries its own EF Core migrations, recorded in the `__xapi_migrations` table.
`MigrateOnStartup = true` applies them when the host starts, before the server accepts requests. It is off by
default, so production deployments can migrate as a separate step, for example before a rolling update. If
the database is not migrated and `MigrateOnStartup` is off, startup fails with a message that says so.

A separate step can run the same app with a switch, before `app.Run()`:

<!-- snippet: lrs-migrate -->
```csharp
if (args.Contains("--migrate"))
{
    await app.Services.MigrateXapiLrsDatabaseAsync();
    return;
}
```

`MigrateXapiLrsDatabaseAsync` applies the same migrations as `MigrateOnStartup`, and works on any service
provider that `AddXapiLrs` was called on, so a separate migration tool can call it too.

## Authentication

### Basic credentials

By default the LRS authenticates requests with HTTP Basic and the credentials in `Credentials`. At startup
they are written to the database with the secret hashed (PBKDF2), and the stored list is made to match the
configured one, so a credential removed from the configuration stops working. Secrets are never stored or
logged in plain text. Keep them out of source control, in configuration, environment variables or a secret
store.

A request without valid credentials gets 401 with a `WWW-Authenticate: Basic` challenge. A request whose credential lacks
the scope for the resource gets 403.

Basic sends the secret with every request, so serve the LRS over HTTPS only. Checking a secret runs PBKDF2,
which is slow on purpose: an unknown key costs the same check as a wrong secret, so response times do not tell
which keys exist, and every failed attempt costs CPU time. The LRS does not limit attempts itself. Add rate
limiting (see [Endpoint conventions](#endpoint-conventions)), partitioned by client address if the LRS is
reachable from the internet, so that guessing secrets is slow and cannot tie up the server. A successful check
is remembered in memory for one minute; with several app instances on one database, a credential removed from
the configuration stops working on the other instances within a minute of the restart that removed it.

| xAPI scope | `XapiScopes` | Allows |
| --- | --- | --- |
| `statements/write` | `StatementsWrite` | Storing statements. |
| `statements/read/mine` | `StatementsReadMine` | Reading statements stored with the same authority. |
| `statements/read` | `StatementsRead` | Reading all statements. |
| `state` | `State` | Reading and writing State documents. |
| `profile` | `Profile` | Reading and writing Activity and Agent Profile documents, and reading the Agents and Activities Resources. |
| `define` | `Define` | Statements stored with this scope update the LRS's Activity definitions; without it they are stored but do not change them. |
| `all/read` | `AllRead` | Reading everything. |
| `all` | `All` | Everything. |

The LRS sets the `authority` of every statement it stores. For a Basic credential it is an Agent with an
account on `AuthorityHomePage` named after the key, unless the credential was added in code with an authority
(`Credentials.Add(key, secret, authority, scopes)`).

### Delegated authentication

An app that already authenticates its users, for example with JWT bearer tokens, can let the LRS use that
scheme instead:

<!-- snippet: lrs-delegated -->
```csharp
// The app registers the scheme itself, for example with AddJwtBearer() from
// Microsoft.AspNetCore.Authentication.JwtBearer
builder.Services
    .AddXapiLrs(options =>
    {
        options.AuthenticationScheme = "Bearer";
        options.ScopeClaimType = "scope";
        options.AuthorityHomePage = new Uri("https://login.example.com/");
    })
    .UseSqlite("Data Source=lrs.db");
```

The LRS then authenticates each request with that scheme and ignores `Credentials`. A request that fails
authentication gets the scheme's challenge. Scopes come from the `ScopeClaimType` claims, as space-separated
xAPI scope names such as `statements/write statements/read/mine`; names it does not know are ignored. The
authority comes from `AuthorityFactory`, or, when that is not set, is an Agent with an account on
`AuthorityHomePage` named after the principal's `NameIdentifier` claim, its `sub` claim or its name, in that
order. A principal without any of them gets 403.

### How this fits with the app's own authentication

The LRS authenticates inside its endpoints, with its own scheme (`XapiBasic`, or the one in
`AuthenticationScheme`). It does not change the app's default scheme and does not need
`UseAuthentication()`. If the app sets an authorization fallback policy, that policy also applies to the LRS
endpoints, which have no authorization metadata of their own; add `.AllowAnonymous()` to the group returned by
`MapXapiLrs` so that only the LRS's own checks apply.

## Endpoint conventions

`MapXapiLrs` returns the `RouteGroupBuilder` of the LRS endpoints, so rate limiting and other endpoint
conventions are added the usual way:

<!-- snippet: lrs-rate-limiting -->
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("xapi", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromSeconds(1);
    });
});

var app = builder.Build();
app.UseRateLimiter();
app.MapXapiLrs("/xapi").RequireRateLimiting("xapi");
```

429 is the status the [outbox](outbox.md) and most clients treat as "try again later". Responses that
middleware produces before the LRS endpoints run, such as this rejection, do not carry the
`X-Experience-API-Version` header.

### Browser clients on other origins

The LRS sends no CORS headers by default. xAPI content running in a browser on another origin, such as a course
package on a CDN, needs a CORS policy on the LRS endpoints:

<!-- snippet: lrs-cors -->
```csharp
builder.Services.AddCors(options => options.AddPolicy("xapi", policy => policy
    .WithOrigins("https://course.example.com")
    .WithMethods("GET", "HEAD", "PUT", "POST", "DELETE")
    .WithHeaders("Authorization", "Content-Type", "X-Experience-API-Version", "If-Match", "If-None-Match")
    .WithExposedHeaders("ETag", "Last-Modified", "X-Experience-API-Version", "X-Experience-API-Consistent-Through")));

var app = builder.Build();
app.UseCors();
app.MapXapiLrs("/xapi").RequireCors("xapi");
```

List the origins you serve content from rather than allowing any origin. The exposed headers let the client
read ETags for document concurrency and the version and consistency headers.

If `AuthenticationScheme` is a cookie scheme and the cookie is sent on cross-site requests (`SameSite=None`), a
page on another site can make the browser send it with a form post. xAPI 2.0 requests need the `X-Experience-API-Version` header, which a cross-site form cannot set,
but the xAPI 1.0.3 Alternate Request Syntax carries headers as form fields. Set `EnableVersion1_0_3 = false`,
or require antiforgery tokens, when cookies authenticate the LRS.

### Content served back to browsers

Documents are returned with the content type the client stored them with, which can be `text/html` or
`image/svg+xml`. Every LRS response has `X-Content-Type-Options: nosniff`, and a document that is not
`application/json` also comes with `Content-Disposition: attachment` and `Content-Security-Policy: default-src
'none'; sandbox`, so a browser opened on its URL downloads it instead of running it on your app's origin. If
clients you do not trust can write documents, consider mapping the LRS on a host name of its own anyway.

## Limits

| Limit | Where it is set | Over the limit |
| --- | --- | --- |
| Document size | `MaxDocumentBytes` (1 MiB) | 413 |
| Statement JSON per statement request | `MaxStatementBytes` (16 MiB) | 413 |
| Attachment data per statement request | `MaxAttachmentBytes` (50 MiB) | 413 |
| Statements per POST | `MaxStatementsPerBatch` (1,000) | 413 |
| Form of an Alternate Request Syntax request | `MaxAlternateRequestBytes` (1 MiB) | 413 |
| Statements per query page | `MaxStatementsPerPage` (100) | The page is shorter and has a `more` link. |
| Request body of other requests | The server, for example Kestrel's `MaxRequestBodySize` (30,000,000 bytes by default) | 413 |

The options above replace the server's request body limit on the requests they cover. Document writes get
`MaxDocumentBytes` as their limit, `application/json` statement requests `MaxStatementBytes`, and
`multipart/mixed` statement requests `MaxStatementBytes` plus `MaxAttachmentBytes` plus 1 MiB for part headers
and boundaries. So raising `MaxAttachmentBytes` above Kestrel's default works without changing Kestrel, and a
lower global limit set in Kestrel does not apply to these requests. The LRS sets the limit through
`IHttpMaxRequestBodySizeFeature`; on a server without it, or when other middleware has already read the body,
the server's own limit stays in place and the LRS still enforces its options while reading.

A statement request is read into memory whole before it is validated, so one request can hold up to
`MaxStatementBytes` plus `MaxAttachmentBytes` of memory, and more while it is parsed. Lower the limits if many
large requests can arrive at once.

`MaxStatementsPerBatch` is checked by counting the top-level array before anything is parsed. A batch is parsed
in memory (about 20 times its JSON size) and stored in one transaction, during which SQLite lets no other write
through; at 1,000 statements of about 1 KB that is roughly 20 MB and 0.3 to 0.5 seconds on the laptop in
[benchmarks.md](benchmarks.md). The ADL conformance suite sends batches of at most 2 statements, and the outbox in
`Xapi.Client.Outbox` sends 50 by default. The answer is 413 rather than 400 because the batch is valid, only
larger than this LRS accepts; the client can send it in parts.

The Alternate Request Syntax of xAPI 1.0.3 carries the credentials as a form field, so the LRS has to read the
form before it can authenticate the request. `MaxAlternateRequestBytes` is therefore what an anonymous client can
make the LRS read and hold, and it is kept well below the other limits. The syntax is meant for browsers that
cannot send other methods or headers across origins, which send single statements and small documents. The
statements or document in the form must still fit `MaxStatementBytes` or `MaxDocumentBytes`.

Responses with `attachments=true` are written as they are read: the statements first, then the data of one
attachment at a time, so a response holds at most one attachment in memory.

## xAPI versions

Requests with `X-Experience-API-Version` `2.0` or `2.0.x` are handled as xAPI 2.0.0, and `1.0` or `1.0.x` as
1.0.3 (unless `EnableVersion1_0_3` is off). Any other value, or a missing header, gets 400, except on
`/about`. [versions.md](versions.md) lists what differs between the two.

## Logging

The LRS logs when it applies migrations, unhandled exceptions (with the method and path, not the query
string), and, at Debug level, writes retried after a concurrent write.
EF Core logs under the `Microsoft.EntityFrameworkCore` categories.

Query strings of LRS requests carry agents as JSON, often with an email address (`mbox`). ASP.NET Core logs
every request URL, query string included, under `Microsoft.AspNetCore.Hosting.Diagnostics` at Information
level. Set that category to `Warning` in production, or log requests with HTTP logging and leave the query
string out. Error responses never contain stack traces or SQL; unexpected errors are logged and answered with a
generic 500.

## Metrics and traces

The LRS reports metrics on the `Xapi.Lrs` meter and traces on the `Xapi.Lrs` activity source, with
`System.Diagnostics` and no OpenTelemetry dependency. To export them with the OpenTelemetry .NET SDK, add both
names on `builder.Services.AddOpenTelemetry()`: `.WithMetrics(m => m.AddMeter(XapiLrsDiagnostics.MeterName))`
and `.WithTracing(t => t.AddSource(XapiLrsDiagnostics.ActivitySourceName))`, next to the ASP.NET Core and EF Core
instrumentation. `XapiLrsDiagnostics` holds every name on this page as a constant. The meter is created through
the app's `IMeterFactory`. When nothing listens, the LRS records nothing and starts no activities.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `xapi.lrs.request.duration` | Histogram | `s` | `xapi.resource`, `http.request.method`, `http.response.status_code`, `xapi.version`, `error.type` |
| `xapi.lrs.errors` | Counter | `{error}` | The same tags; `error.type` is always set. |
| `xapi.lrs.statements.stored` | Counter | `{statement}` | `xapi.version` |

`xapi.lrs.request.duration` is the time from the start of an LRS endpoint to the end of its response:
authentication, version negotiation, the database and writing the body, but not middleware that runs before
the endpoint, such as rate limiting or CORS. ASP.NET Core's own `http.server.request.duration` measures the
whole request with the route template (`http.route`, which includes the prefix); this histogram adds what that
one cannot know, the xAPI resource and the negotiated version, and uses the same bucket boundaries.

`xapi.lrs.errors` counts the requests the LRS endpoints answered with a 4xx or 5xx status.
`xapi.lrs.statements.stored` counts the statements of the PUT and POST requests the LRS accepted. A statement
sent again that is equivalent to the stored one is accepted and counted again; a batch rejected with 409 is not
counted.

| Tag | Values |
| --- | --- |
| `xapi.resource` | `about`, `statements`, `agents`, `activities`, `activities/state`, `activities/profile`, `agents/profile`, or `_OTHER` for a path that is not an xAPI resource |
| `http.request.method` | `GET`, `HEAD`, `PUT`, `POST`, `DELETE` and the other standard methods, or `_OTHER` |
| `http.response.status_code` | The status code, as a number |
| `xapi.version` | `2.0.0` or `1.0.3`, the version the request was handled with. Missing when the request was refused before a version was negotiated. |
| `error.type` | The status code as text (`400`, `503`), or the full type name of the exception that ended the request. On the histogram only for 5xx and exceptions, as in OpenTelemetry's HTTP conventions. |

The activities nest under ASP.NET Core's request activity, and EF Core's database activities nest under them:

| Activity | Started for | Tags |
| --- | --- | --- |
| `xapi.lrs.statements.store` | Storing the statements of a PUT or POST | `xapi.version`, `xapi.statements.count`, `xapi.statements.conflict` |
| `xapi.lrs.statements.query` | Reading a page of statements for a query or a `more` link | `xapi.version`, `xapi.statements.count` (returned) |
| `xapi.lrs.documents.write` | A State, Activity Profile or Agent Profile PUT, POST or DELETE | `xapi.resource`, `xapi.document.operation` (`replace`, `merge` or `delete`) |

An activity whose operation throws gets the status `Error` and an `error.type` tag with the exception's type
name. No tag holds data from the request: agents, email addresses, IRIs, statement and document ids,
registrations and credential keys never appear in a tag, and exception messages, which can quote stored values,
are not recorded.

## Health checks

`AddXapiLrsHealthCheck` adds a check that the database can be reached and has every migration of the provider
package applied:

<!-- snippet: lrs-health -->
```csharp
builder.Services.AddHealthChecks().AddXapiLrsHealthCheck(tags: ["ready"]);

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
```

A database that cannot be reached and one with migrations that are not applied both report the failure status,
`Unhealthy` unless `failureStatus` says otherwise, because requests to the LRS fail in both cases. During a
rolling update, instances of the new version report it until the migration step has run, so a readiness probe
keeps them out of rotation; instances of the old version are not affected by migrations they do not know. Each
check opens a connection and reads the migrations history table.

## Running the sample LRS in Docker

Each release publishes the standalone sample as `ghcr.io/nurullahturkoglu/xapi-lrs`, for linux/amd64 and
linux/arm64, tagged with the release version (`0.1.0`, `0.1.0-preview.1`) and, for releases that are not
previews, `latest`. The image runs as a non-root user, stores SQLite data in the `/data` volume and does not
start until a credential is given:

```bash
docker run -p 8080:8080 -e LrsHost__Key=my-key -e LrsHost__Secret=my-secret -v xapi-lrs-data:/data \
  ghcr.io/nurullahturkoglu/xapi-lrs:latest
```

Each image has a signed build provenance attestation that ties it to the release workflow run and the tagged
commit. The GitHub CLI checks it:

```bash
gh attestation verify oci://ghcr.io/nurullahturkoglu/xapi-lrs:latest --repo nurullahturkoglu/xapi-dotnet
```

To build the image from the repository instead, run `samples/LrsHost/Dockerfile` from the repository root:

```bash
docker build -f samples/LrsHost/Dockerfile -t xapi-lrs .
docker run -p 8080:8080 -e LrsHost__Key=my-key -e LrsHost__Secret=my-secret -v xapi-lrs-data:/data xapi-lrs
```

The LRS is then at `http://localhost:8080/xapi`, and `http://localhost:8080/health` answers `Healthy` once the
database is reachable and migrated. The sample's own settings are in the `LrsHost` section: for PostgreSQL,
add `-e LrsHost__Database=postgres` and `-e LrsHost__ConnectionString=...`. `LrsHost__Key` and
`LrsHost__Secret` give one credential with every scope. The `Lrs` section is bound to the LRS options, so
`-e Lrs__AuthorityHomePage=...`, `-e Lrs__Credentials__0__Key=...` and the other keys from
[Options from configuration](#options-from-configuration) work too.

## Experimental storage API

The storage interfaces are public so that the provider packages and custom stores can use them. They are
`IStatementStore`, `IDocumentStore`, `XapiLrsDatabaseProvider`, `XapiLrsDbContext`, the types they use, and the
builder methods `UseStatementStore<TStore>`, `UseDocumentStore<TStore>` and `UseDatabaseProvider`. All of them
are marked `[Experimental("XAPI0001")]`.

They are not covered by semantic versioning, before or after 1.0: any release, minor and patch releases
included, can change or remove them. They stay experimental past 1.0 until they are declared stable, one at a
time, in the changelog. The rest of the LRS API, including `AddXapiLrs`, `MapXapiLrs`, `XapiLrsOptions`,
`MigrateXapiLrsDatabaseAsync`, `UseSqlite` and `UsePostgreSql`, follows semantic versioning as usual. An app
that only uses those never sees `XAPI0001`.

Code that uses an experimental type gets the compile error `XAPI0001`. Suppressing it, with
`<NoWarn>$(NoWarn);XAPI0001</NoWarn>` in the project file or `#pragma warning disable XAPI0001` around the code,
accepts that the code may have to change when `Xapi.Lrs` is updated. `Xapi.Lrs.Sqlite` and
`Xapi.Lrs.PostgreSql` use these types themselves, so they are released together with `Xapi.Lrs` and depend on
exactly the same version of it.

A custom `IStatementStore` should know three things about the data it is given:

- Statement JSON is UTF-8 (`StatementRecord.Utf8Json`). The store returns the same bytes in
  `StoredStatement.Utf8Json`, and the LRS writes them into responses without parsing them again.
- `StatementPage.ContinuationToken` is the store's own opaque text. The LRS puts it into the `more` URL and
  hands it back in `StatementFilter.ContinuationToken`, so a client can change it; the store throws
  `ArgumentException` for a token it cannot read, and the LRS answers 400.
- SHA-2 hashes of attachment data are lower-case hexadecimal (`AttachmentContent.Sha2`), and lookups may come in
  either case.

[ADR-0021](adr/0021-storage-api-before-stable.md) lists what is still expected to change before each part of
this API is declared stable. `IDocumentStore` is the first candidate. `XapiLrsDbContext` and
`XapiLrsDatabaseProvider` expose EF Core types and are not planned to become stable; apps migrate with
`MigrateXapiLrsDatabaseAsync` instead.
