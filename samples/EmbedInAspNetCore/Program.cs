// An existing ASP.NET Core app that adds an LRS under /xapi next to its own endpoints. The LRS keeps its data in
// SQLite and reads its options, Basic credentials included, from the Lrs section of configuration:
// appsettings.json, environment variables (Lrs__Credentials__0__Secret) or user secrets.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiLrs(builder.Configuration.GetSection("Lrs"), options => options.MigrateOnStartup = true)
    .UseSqlite(builder.Configuration.GetConnectionString("Lrs") ?? "Data Source=lrs.db");

var app = builder.Build();

// The app's own endpoints
app.MapGet("/", () => "Course catalog. The catalog is at /api/courses, the LRS at /xapi.");
app.MapGet("/api/courses", () => Courses.All);

app.MapXapiLrs("/xapi");

app.Run();

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
