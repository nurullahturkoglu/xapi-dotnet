// A standalone LRS: the target of the conformance runs (eng/conformance) and a starting point for hosting
// Xapi.Lrs. Settings come from appsettings.json, environment variables (Lrs__Database=postgres) or arguments
// (--Lrs:Database=postgres):
//   Lrs:Database          sqlite (default) or postgres
//   Lrs:ConnectionString  defaults to Data Source=lrs.db for sqlite; required for postgres
//   Lrs:Key, Lrs:Secret   the Basic credential, granted every scope
//   Lrs:AuthorityHomePage the homePage of generated authorities
using Xapi;
using Xapi.Lrs;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Lrs");

var key = settings["Key"];
var secret = settings["Secret"];
if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
{
    throw new InvalidOperationException("Set Lrs:Key and Lrs:Secret.");
}

var lrs = builder.Services.AddXapiLrs(options =>
{
    options.MigrateOnStartup = true;
    options.Credentials.Add(key, secret, XapiScope.All);
    if (settings["AuthorityHomePage"] is { Length: > 0 } homePage)
    {
        options.AuthorityHomePage = new Iri(homePage);
    }
});

var database = settings["Database"] ?? "sqlite";
var connectionString = settings["ConnectionString"];
switch (database)
{
    case "sqlite":
        lrs.UseSqlite(string.IsNullOrEmpty(connectionString) ? "Data Source=lrs.db" : connectionString);
        break;
    case "postgres":
        lrs.UsePostgreSql(string.IsNullOrEmpty(connectionString)
            ? throw new InvalidOperationException("Set Lrs:ConnectionString for PostgreSQL.")
            : connectionString);
        break;
    default:
        throw new InvalidOperationException($"Lrs:Database must be sqlite or postgres, not '{database}'.");
}

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.Run();

/// <summary>The entry point, public so tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
