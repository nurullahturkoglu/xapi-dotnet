# Xapi.Lrs.PostgreSql

PostgreSQL storage for the `Xapi.Lrs` Learning Record Store (xAPI 2.0, IEEE 9274.1.1, and 1.0.3):
`UsePostgreSql` and migrations. Use it when several app instances share one database.

```bash
dotnet add package Xapi.Lrs.PostgreSql --prerelease
```

## Usage

In `Program.cs`, with `using Xapi.Lrs;`:

<!-- snippet: lrs-postgresql -->
```csharp
builder.Services
    .AddXapiLrs(options => options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScope.All))
    .UsePostgreSql(builder.Configuration.GetConnectionString("Lrs")!);
```

Then map the endpoints with `app.MapXapiLrs("/xapi")`. PostgreSQL cannot index values over about 2.7 KB, so a
request with a longer IRI, agent identifier or document id gets 400.

## Target frameworks

`net10.0`. This package depends on the same `Xapi.Lrs` version it was built with.

## Links

- [LRS guide](https://nurullahturkoglu.github.io/xapi-dotnet/lrs.html): storage, migrations, options
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.Lrs.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
