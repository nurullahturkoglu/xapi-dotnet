# Xapi.Lrs.Sqlite

SQLite storage for the `Xapi.Lrs` Learning Record Store (xAPI 2.0, IEEE 9274.1.1, and 1.0.3): `UseSqlite`,
migrations, WAL mode and serialized statement writes. It suits an LRS on one server. For several app instances
sharing one database, use `Xapi.Lrs.PostgreSql`.

```bash
dotnet add package Xapi.Lrs.Sqlite --prerelease
```

## Usage

In `Program.cs`, with `using Xapi.Lrs;`:

<!-- snippet: host-lrs -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiLrs(options =>
    {
        options.MigrateOnStartup = true;
        options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScope.All);
    })
    .UseSqlite("Data Source=lrs.db");

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.Run();
```

## Target frameworks

`net10.0`. This package depends on the same `Xapi.Lrs` version it was built with.

## Links

- [LRS guide](https://nurullahturkoglu.github.io/xapi-dotnet/lrs.html): storage, migrations, options
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.Lrs.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
