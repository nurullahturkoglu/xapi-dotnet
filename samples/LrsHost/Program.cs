// A standalone LRS: the target of the conformance runs (eng/conformance) and a starting point for hosting
// Xapi.Lrs. Settings come from appsettings.json, environment variables (Lrs__Database=postgres) or arguments
// (--Lrs:Database=postgres):
//   Lrs:Database          sqlite (default) or postgres
//   Lrs:ConnectionString  defaults to Data Source=lrs.db for sqlite; required for postgres
//   Lrs:Key, Lrs:Secret   a Basic credential granted every scope
//   Lrs:Credentials       more credentials, for example Lrs:Credentials:0:Key, :Secret and :Scopes:0
//   Lrs:AuthorityHomePage the homePage of generated authorities
// and the other XapiLrsOptions by name, for example Lrs:MaxStatementsPerPage (XapiLrsOptions.Bind).
using Xapi.Lrs;

// As PID 1 in a container the process ignores the SIGABRT that ends an unhandled exception and spins instead of
// exiting, so report the exception and exit explicitly
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.Error.WriteLine($"Unhandled exception. {e.ExceptionObject}");
    Environment.Exit(1);
};

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Lrs");

var key = settings["Key"];
var secret = settings["Secret"];
if (string.IsNullOrEmpty(key) != string.IsNullOrEmpty(secret))
{
    throw new InvalidOperationException("Set both Lrs:Key and Lrs:Secret.");
}

if (string.IsNullOrEmpty(key) && !settings.GetSection("Credentials").Exists())
{
    throw new InvalidOperationException("Set Lrs:Key and Lrs:Secret, or list credentials under Lrs:Credentials.");
}

var lrs = builder.Services.AddXapiLrs(options =>
{
    options.MigrateOnStartup = true;
    options.Bind(settings);
    if (!string.IsNullOrEmpty(key))
    {
        options.Credentials.Add(key, secret!, XapiScope.All);
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
