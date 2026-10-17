using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Xapi.Lrs.Tests;

// The CORS setup docs/lrs.md recommends for browser clients on other origins: preflight requests reach the
// policy even though the LRS maps a catch-all route for 404 and 405 answers.
public class CorsTests
{
    private const string Origin = "https://app.example.com";

    [Theory]
    [InlineData("/xapi/statements", "PUT")]
    [InlineData("/xapi/statements", "GET")]
    [InlineData("/xapi/activities/state", "PUT")]
    [InlineData("/xapi/about", "GET")]
    public async Task A_cors_policy_on_the_route_group_answers_preflight_requests(string path, string method)
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Path.Combine(Path.GetTempPath(), $"xapi-cors-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddCors(o => o.AddPolicy("xapi", policy => policy
            .WithOrigins(Origin)
            .WithMethods("GET", "HEAD", "PUT", "POST", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "X-Experience-API-Version", "If-Match", "If-None-Match")
            .WithExposedHeaders("ETag", "Last-Modified", "X-Experience-API-Version", "X-Experience-API-Consistent-Through")));
        builder.Services
            .AddXapiLrs(o =>
            {
                o.MigrateOnStartup = true;
                o.Credentials.Add("key", "secret", XapiScopes.All);
            })
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();
        app.UseCors();
        app.MapXapiLrs().RequireCors("xapi");
        await app.StartAsync(ct);

        try
        {
            var client = app.GetTestClient();
            var preflight = new HttpRequestMessage(HttpMethod.Options, path);
            preflight.Headers.Add("Origin", Origin);
            preflight.Headers.Add("Access-Control-Request-Method", method);
            preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-experience-api-version");
            using var response = await client.SendAsync(preflight, ct);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

            var get = new HttpRequestMessage(HttpMethod.Get, "/xapi/about");
            get.Headers.Add("Origin", Origin);
            using var about = await client.SendAsync(get, ct);
            Assert.Equal(HttpStatusCode.OK, about.StatusCode);
            Assert.Equal(Origin, Assert.Single(about.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Contains("X-Experience-API-Version", about.Headers.GetValues("Access-Control-Expose-Headers").Single(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync(ct);
            File.Delete(database);
        }
    }

    // samples/LrsHost sets up the same policy from LrsHost:AllowedOrigins
    [Theory]
    [InlineData(Origin, Origin, Origin)]
    [InlineData("https://other.example.com; " + Origin, Origin, Origin)]
    [InlineData("*", Origin, "*")]
    [InlineData(Origin, "https://other.example.com", null)]
    [InlineData(null, Origin, null)]
    public async Task LrsHost_allows_the_origins_in_its_settings(string? allowedOrigins, string origin, string? expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Path.Combine(Path.GetTempPath(), $"xapi-cors-{Guid.NewGuid():N}.db");
        try
        {
            await using (var factory = LrsHost(database, allowedOrigins))
            {
                var client = factory.CreateClient();
                var preflight = new HttpRequestMessage(HttpMethod.Options, "/xapi/statements");
                preflight.Headers.Add("Origin", origin);
                preflight.Headers.Add("Access-Control-Request-Method", "PUT");
                preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-experience-api-version");
                using var response = await client.SendAsync(preflight, ct);

                Assert.Equal(expected, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? Assert.Single(values) : null);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/course")]
    [InlineData("app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("*;https://app.example.com")]
    public async Task LrsHost_does_not_start_with_an_allowed_origin_that_cannot_match(string allowedOrigins)
    {
        var database = Path.Combine(Path.GetTempPath(), $"xapi-cors-{Guid.NewGuid():N}.db");
        await using var factory = LrsHost(database, allowedOrigins);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Server);

        Assert.Contains("LrsHost:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> LrsHost(string database, string? allowedOrigins) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LrsHost:ConnectionString", $"Data Source={database}");
            builder.UseSetting("LrsHost:Key", "key");
            builder.UseSetting("LrsHost:Secret", "secret");
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            if (allowedOrigins is not null)
            {
                builder.UseSetting("LrsHost:AllowedOrigins", allowedOrigins);
            }
        });
}
