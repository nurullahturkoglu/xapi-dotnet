# Xapi.Lrs

An embeddable Experience API (xAPI 2.0, IEEE 9274.1.1, and 1.0.3) Learning Record Store for ASP.NET Core:
minimal API endpoints under a path prefix, version negotiation, Basic or delegated authentication with xAPI
scopes, and EF Core storage. It passes the ADL LRS Conformance Test Suite for both versions.

This package needs a database provider, and the provider brings it in. Install one of them:

```bash
dotnet add package Xapi.Lrs.Sqlite
dotnet add package Xapi.Lrs.PostgreSql
```

## Usage

In `Program.cs`, with the SQLite provider:

<!-- snippet: host-lrs -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiLrs(builder.Configuration.GetSection("Lrs"), options => options.MigrateOnStartup = true)
    .UseSqlite("Data Source=lrs.db");

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.Run();
```

`appsettings.json` lists the credentials under `Lrs`, without their secrets:

```json
{
  "Lrs": {
    "Credentials": [ { "Key": "my-app", "Scopes": [ "all" ] } ]
  }
}
```

The secret comes from user secrets or the environment (`Lrs__Credentials__0__Secret`). Clients then use
`https://<your app>/xapi/` as their endpoint, with the key `my-app` and that secret.

## Target frameworks

`net10.0`. EF Core cannot be trimmed or published with Native AOT, so neither can this package.

## Links

- [LRS guide](https://nurullahturkoglu.github.io/xapi-dotnet/lrs.html): options, storage, migrations, authentication, limits
- [Conformance](https://nurullahturkoglu.github.io/xapi-dotnet/conformance.html)
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.Lrs.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
