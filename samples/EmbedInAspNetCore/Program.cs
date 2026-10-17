// An existing ASP.NET Core app that adds an LRS under /xapi next to its own endpoints. The LRS keeps its data in
// SQLite and reads its Basic credentials from configuration: appsettings.json, environment variables
// (Lrs__Credentials__0__Secret) or user secrets.
using Xapi;
using Xapi.Lrs;

var builder = WebApplication.CreateBuilder(args);

var credentials = builder.Configuration.GetSection("Lrs:Credentials").Get<LrsCredential[]>() ?? [];
if (credentials.Length == 0)
{
    throw new InvalidOperationException("Configure at least one credential under Lrs:Credentials.");
}

builder.Services
    .AddXapiLrs(options =>
    {
        options.MigrateOnStartup = true;
        if (builder.Configuration["Lrs:AuthorityHomePage"] is { Length: > 0 } homePage)
        {
            options.AuthorityHomePage = new Iri(homePage);
        }

        foreach (var credential in credentials)
        {
            options.Credentials.Add(credential.Key, credential.Secret, credential.Scopes);
        }
    })
    .UseSqlite(builder.Configuration.GetConnectionString("Lrs") ?? "Data Source=lrs.db");

var app = builder.Build();

// The app's own endpoints
app.MapGet("/", () => "Course catalog. The catalog is at /api/courses, the LRS at /xapi.");
app.MapGet("/api/courses", () => Courses.All);

app.MapXapiLrs("/xapi");

app.Run();

/// <summary>A Basic credential of the LRS, bound from <c>Lrs:Credentials</c>.</summary>
/// <param name="Key">The Basic user name.</param>
/// <param name="Secret">The Basic password.</param>
/// <param name="Scopes">The scopes granted, by <see cref="XapiScope"/> name.</param>
internal sealed record LrsCredential(string Key, string Secret, XapiScope[] Scopes);

/// <summary>A course of the app. Its id is also the xAPI Activity id that statements about it use.</summary>
/// <param name="Id">The Activity id.</param>
/// <param name="Title">The title.</param>
internal sealed record Course(string Id, string Title);

/// <summary>The app's own data, standing in for a real catalog.</summary>
internal static class Courses
{
    public static Course[] All { get; } =
    [
        new("https://courses.example.com/fire-safety", "Fire safety"),
        new("https://courses.example.com/first-aid", "First aid"),
    ];
}
