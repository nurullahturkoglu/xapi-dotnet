// A standalone LRS: the target of the conformance runs (eng/conformance) and a starting point for hosting
// Xapi.Lrs. Settings come from appsettings.json, environment variables (LrsHost__Database=postgres) or arguments
// (--LrsHost:Database=postgres). The host's own settings are in the LrsHost section:
//   LrsHost:Database          sqlite (default) or postgres
//   LrsHost:ConnectionString  defaults to Data Source=lrs.db for sqlite; required for postgres
//   LrsHost:Key, :Secret      a Basic credential granted every scope
// The Lrs section holds XapiLrsOptions by name, for example Lrs:AuthorityHomePage, Lrs:MaxStatementsPerPage and
// more credentials as Lrs:Credentials:0:Key, :Secret and :Scopes:0. An unknown key in it stops the host.
// GET /health answers 200 Healthy when the database can be reached and is migrated, and 503 otherwise.
using Xapi.Lrs;

// As PID 1 in a container the process ignores the SIGABRT that ends an unhandled exception and spins instead of
// exiting, so report the exception and exit explicitly
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.Error.WriteLine($"Unhandled exception. {e.ExceptionObject}");
    Environment.Exit(1);
};

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("LrsHost");

var key = settings["Key"];
var secret = settings["Secret"];
if (string.IsNullOrEmpty(key) != string.IsNullOrEmpty(secret))
{
    throw new InvalidOperationException("Set both LrsHost:Key and LrsHost:Secret.");
}

if (string.IsNullOrEmpty(key) && !builder.Configuration.GetSection("Lrs:Credentials").Exists())
{
    throw new InvalidOperationException("Set LrsHost:Key and LrsHost:Secret, or list credentials under Lrs:Credentials.");
}

// A default that Lrs:MigrateOnStartup can override, because it is applied before the section is bound
builder.Services.Configure<XapiLrsOptions>(options => options.MigrateOnStartup = true);
var lrs = builder.Services.AddXapiLrs(builder.Configuration.GetSection("Lrs"), options =>
{
    if (!string.IsNullOrEmpty(key))
    {
        options.Credentials.Add(key, secret!, XapiScopes.All);
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
            ? throw new InvalidOperationException("Set LrsHost:ConnectionString for PostgreSQL.")
            : connectionString);
        break;
    default:
        throw new InvalidOperationException($"LrsHost:Database must be sqlite or postgres, not '{database}'.");
}

builder.Services.AddHealthChecks().AddXapiLrsHealthCheck();

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.MapHealthChecks("/health");
app.Run();

/// <summary>The entry point, public so tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
