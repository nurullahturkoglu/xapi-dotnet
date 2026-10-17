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
        options.Credentials.Add("course-player", builder.Configuration["Lrs:PlayerSecret"]!,
            XapiScope.StatementsWrite, XapiScope.StatementsReadMine, XapiScope.State);
        options.Credentials.Add("reporting", builder.Configuration["Lrs:ReportingSecret"]!, XapiScope.AllRead);
        options.Credentials.Add(new XapiCredential("lms", builder.Configuration["Lrs:LmsSecret"]!, [XapiScope.All])
        {
            Authority = Agent.FromAccount(new Iri("https://lms.example.com/"), "lms"),
        });

        options.AuthorityHomePage = new Iri("https://lrs.example.com/");
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
| `MaxStatementsPerPage` | 100 | The page size of statement queries when the request asks for `limit=0` or no limit; larger limits are lowered to it. |
| `MigrateOnStartup` | `false` | Apply database migrations when the host starts. |

## Storage

| Package | Builder method | Notes |
| --- | --- | --- |
| `Xapi.Lrs.Sqlite` | `UseSqlite(connectionString)` | WAL mode and a 30 second busy timeout. Statement writes take the write lock when their transaction begins (`BEGIN IMMEDIATE`), so concurrent writers wait for each other instead of failing. Suits one server. |
| `Xapi.Lrs.PostgreSql` | `UsePostgreSql(connectionString)` | For several app instances sharing one database. PostgreSQL cannot index values over about 2.7 KB, so a statement or document request with a longer IRI, agent identifier or document id gets 400. |

<!-- snippet: lrs-postgresql -->
```csharp
builder.Services
    .AddXapiLrs(options => options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScope.All))
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
#pragma warning disable XAPI0001 // XapiLrsDbContext is part of the experimental storage API
if (args.Contains("--migrate"))
{
    var contexts = app.Services.GetRequiredService<IDbContextFactory<XapiLrsDbContext>>();
    await using var context = await contexts.CreateDbContextAsync();
    await context.Database.MigrateAsync();
    return;
}
#pragma warning restore XAPI0001
```

`XapiLrsDbContext` belongs to the experimental storage API (see below), so the snippet suppresses `XAPI0001`.

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

| `XapiScope` | xAPI scope | Allows |
| --- | --- | --- |
| `StatementsWrite` | `statements/write` | Storing statements. |
| `StatementsReadMine` | `statements/read/mine` | Reading statements stored with the same authority. |
| `StatementsRead` | `statements/read` | Reading all statements. |
| `State` | `state` | Reading and writing State documents. |
| `Profile` | `profile` | Reading and writing Activity and Agent Profile documents, and reading the Agents and Activities Resources. |
| `Define` | `define` | Statements stored with this scope update the LRS's Activity definitions; without it they are stored but do not change them. |
| `AllRead` | `all/read` | Reading everything. |
| `All` | `all` | Everything. |

The LRS sets the `authority` of every statement it stores. For a Basic credential it is an Agent with an
account on `AuthorityHomePage` named after the key, unless the credential sets `Authority`.

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
        options.AuthorityHomePage = new Iri("https://login.example.com/");
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
large requests can arrive at once. There is no separate limit on the number of statements in a batch.

## xAPI versions

Requests with `X-Experience-API-Version` `2.0` or `2.0.x` are handled as xAPI 2.0.0, and `1.0` or `1.0.x` as
1.0.3 (unless `EnableVersion1_0_3` is off). Any other value, or a missing header, gets 400, except on
`/about`. [versions.md](versions.md) lists what differs between the two.

## Logging

The LRS logs when it applies migrations, unhandled exceptions (with the method and path, not the query
string), and, at Debug level, writes retried after a concurrent write. It does not emit metrics or traces yet.
EF Core logs under the `Microsoft.EntityFrameworkCore` categories.

Query strings of LRS requests carry agents as JSON, often with an email address (`mbox`). ASP.NET Core logs
every request URL, query string included, under `Microsoft.AspNetCore.Hosting.Diagnostics` at Information
level. Set that category to `Warning` in production, or log requests with HTTP logging and leave the query
string out. Error responses never contain stack traces or SQL; unexpected errors are logged and answered with a
generic 500.

## Running the sample LRS in Docker

`samples/LrsHost/Dockerfile` builds the standalone sample into an image. It runs as a non-root user, stores
SQLite data in the `/data` volume and does not start until a credential is given:

```bash
docker build -f samples/LrsHost/Dockerfile -t xapi-lrs .
docker run -p 8080:8080 -e Lrs__Key=my-key -e Lrs__Secret=my-secret -v xapi-lrs-data:/data xapi-lrs
```

The LRS is then at `http://localhost:8080/xapi`. For PostgreSQL, add `-e Lrs__Database=postgres` and
`-e Lrs__ConnectionString=...`. No image is published yet; build it from the repository.

## Experimental storage API

The storage interfaces are public so that the provider packages and custom stores can use them, but they are
marked `[Experimental("XAPI0001")]` and are not covered by semantic versioning before 1.0. Using them is a
compile error until `XAPI0001` is suppressed. They are `IStatementStore`, `IDocumentStore`,
`XapiLrsDatabaseProvider`, `XapiLrsDbContext`, the types they use, and the builder methods
`UseStatementStore<TStore>`, `UseDocumentStore<TStore>` and `UseDatabaseProvider`. They will be reviewed, and
may change, before they are declared stable.
