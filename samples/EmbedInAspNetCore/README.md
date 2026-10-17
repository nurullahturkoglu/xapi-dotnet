# EmbedInAspNetCore

An ASP.NET Core app with its own endpoints (`/` and `/api/courses`) that also hosts an LRS under `/xapi`. The
LRS stores its data in SQLite (`lrs.db` in the working directory), creates or upgrades the schema on start
(`MigrateOnStartup`), and reads its options and Basic credentials from the `Lrs` configuration section.

## Run it

```sh
dotnet run --project samples/EmbedInAspNetCore
```

The app listens on `http://localhost:5080`. `appsettings.json` defines two credentials:

| Key | Secret | Scopes |
| --- | --- | --- |
| `course-player` | `change-me` | `statements/write`, `statements/read/mine`, `state` |
| `reporting` | `change-me-too` | `all/read` |

Send a statement as the course player:

```sh
curl -u course-player:change-me \
  -H "X-Experience-API-Version: 2.0.0" -H "Content-Type: application/json" \
  -d '{"actor":{"mbox":"mailto:learner@example.com"},"verb":{"id":"http://adlnet.gov/expapi/verbs/completed"},"object":{"id":"https://courses.example.com/fire-safety"}}' \
  http://localhost:5080/xapi/statements
```

Read the statements as reporting, which can read everything but cannot write (a POST with this credential
gets 403):

```sh
curl -u reporting:change-me-too -H "X-Experience-API-Version: 2.0.0" http://localhost:5080/xapi/statements
```

The app's own endpoints need no credential: `curl http://localhost:5080/api/courses`.

## Secrets

The secrets in `appsettings.json` are placeholders. Replace them with user secrets, environment variables or
another configuration source, for example:

```sh
export Lrs__Credentials__0__Secret='a long random value'
export Lrs__Credentials__1__Secret='another long random value'
```

Set `Lrs:AuthorityHomePage` to the app's public address before storing statements you want to keep. It
becomes part of the `authority` stored with every statement and is not rewritten later.

## Use the app's own authentication instead

An app that already issues tokens can let the LRS authenticate with that scheme instead of Basic credentials.
With `Microsoft.AspNetCore.Authentication.JwtBearer` added to the project:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

// ...

builder.Services
    .AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.example.com/";
        options.Audience = "xapi";
    });

builder.Services
    .AddXapiLrs(options =>
    {
        options.MigrateOnStartup = true;
        options.AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;
        // Space-separated xAPI scope names, such as "statements/write state"
        options.ScopeClaimType = "scope";
        // Optional: by default the authority is an account on AuthorityHomePage named after the user's id
        options.AuthorityFactory = user => Agent.FromAccount(
            new Iri("https://login.example.com/"),
            user.FindFirstValue("sub")!);
    })
    .UseSqlite(builder.Configuration.GetConnectionString("Lrs") ?? "Data Source=lrs.db");
```

`Credentials` is ignored when `AuthenticationScheme` is set. See
[docs/lrs.md](../../docs/lrs.md#delegated-authentication) for the details.
