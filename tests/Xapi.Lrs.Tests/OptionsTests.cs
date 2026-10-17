using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Tests.Infrastructure;

namespace Xapi.Lrs.Tests;

public class OptionsTests
{
    private const string Secret = "do-not-print-this-secret";

    [Fact]
    public async Task Startup_fails_with_every_validation_problem_listed()
    {
        var database = Path.Combine(Path.GetTempPath(), $"xapi-options-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddXapiLrs(o =>
            {
                o.MigrateOnStartup = true;
                o.ScopeClaimType = " ";
            })
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();

        try
        {
            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

            Assert.Equal(2, exception.Failures.Count());
            Assert.Contains("add a credential to Credentials", exception.Message, StringComparison.Ordinal);
            Assert.Contains("ScopeClaimType is empty", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(database), "Validation runs before the database is touched.");
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Theory]
    [InlineData("", "AuthenticationScheme is empty")]
    [InlineData(XapiLrsAuthenticationDefaults.AuthenticationScheme, "built-in Basic scheme")]
    public void A_scheme_that_cannot_replace_basic_authentication_is_rejected(string scheme, string expected)
    {
        var options = new XapiLrsOptions { AuthenticationScheme = scheme };
        options.Credentials.Add("key", Secret, XapiScope.All);

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_authority_home_page_is_rejected()
    {
        var options = new XapiLrsOptions { AuthorityHomePage = default };
        options.Credentials.Add("key", Secret, XapiScope.All);

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Contains("AuthorityHomePage", Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Credentials_or_a_scheme_are_enough()
    {
        var basic = new XapiLrsOptions();
        basic.Credentials.Add("key", Secret, XapiScope.All);

        Assert.True(new XapiLrsOptionsValidator().Validate(Options.DefaultName, basic).Succeeded);
        Assert.True(new XapiLrsOptionsValidator().Validate(Options.DefaultName, new XapiLrsOptions { AuthenticationScheme = "Bearer" }).Succeeded);
    }

    [Fact]
    public void Bind_reads_every_option_and_the_credentials()
    {
        var options = new XapiLrsOptions();
        options.Bind(Configuration(new()
        {
            ["Lrs:AuthenticationScheme"] = "Bearer",
            ["Lrs:ScopeClaimType"] = "scp",
            ["Lrs:AuthorityHomePage"] = "https://lrs.example.com/",
            ["Lrs:EnableVersion1_0_3"] = "false",
            ["Lrs:MaxDocumentBytes"] = "2048",
            ["Lrs:MaxStatementBytes"] = "4096",
            ["Lrs:MaxAttachmentBytes"] = "0",
            ["Lrs:MaxStatementsPerBatch"] = "10",
            ["Lrs:MaxAlternateRequestBytes"] = "8192",
            ["Lrs:MaxStatementsPerPage"] = "20",
            ["Lrs:MigrateOnStartup"] = "true",
            ["Lrs:Credentials:0:Key"] = "player",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "statements/write",
            ["Lrs:Credentials:0:Scopes:1"] = "State",
            ["Lrs:Credentials:1:Key"] = "reporting",
            ["Lrs:Credentials:1:Secret"] = Secret,
            ["Lrs:Credentials:1:Scopes"] = "all/read statements/read",
        }));

        Assert.Equal("Bearer", options.AuthenticationScheme);
        Assert.Equal("scp", options.ScopeClaimType);
        Assert.Equal(new Iri("https://lrs.example.com/"), options.AuthorityHomePage);
        Assert.False(options.EnableVersion1_0_3);
        Assert.Equal(2048, options.MaxDocumentBytes);
        Assert.Equal(4096, options.MaxStatementBytes);
        Assert.Equal(0, options.MaxAttachmentBytes);
        Assert.Equal(10, options.MaxStatementsPerBatch);
        Assert.Equal(8192, options.MaxAlternateRequestBytes);
        Assert.Equal(20, options.MaxStatementsPerPage);
        Assert.True(options.MigrateOnStartup);
        Assert.Collection(
            options.Credentials,
            c =>
            {
                Assert.Equal("player", c.Key);
                Assert.Equal([XapiScope.StatementsWrite, XapiScope.State], c.Scopes);
            },
            c =>
            {
                Assert.Equal("reporting", c.Key);
                Assert.Equal([XapiScope.AllRead, XapiScope.StatementsRead], c.Scopes);
            });
    }

    // Keeps Bind in step with the options: a new settable option must be read too, or listed here
    [Fact]
    public void Bind_covers_every_settable_option()
    {
        string[] notFromConfiguration = [nameof(XapiLrsOptions.AuthorityFactory)];
        string[] read =
        [
            nameof(XapiLrsOptions.AuthenticationScheme), nameof(XapiLrsOptions.ScopeClaimType),
            nameof(XapiLrsOptions.AuthorityHomePage), nameof(XapiLrsOptions.EnableVersion1_0_3),
            nameof(XapiLrsOptions.MaxDocumentBytes), nameof(XapiLrsOptions.MaxStatementBytes),
            nameof(XapiLrsOptions.MaxAttachmentBytes), nameof(XapiLrsOptions.MaxStatementsPerBatch),
            nameof(XapiLrsOptions.MaxAlternateRequestBytes), nameof(XapiLrsOptions.MaxStatementsPerPage),
            nameof(XapiLrsOptions.MigrateOnStartup),
        ];

        var settable = typeof(XapiLrsOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name)
            .Except(notFromConfiguration);

        Assert.Equal(read.Order(StringComparer.Ordinal), settable.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Bind_skips_empty_values_and_keeps_what_was_set_in_code()
    {
        var options = new XapiLrsOptions { MaxStatementsPerPage = 7 };
        options.Credentials.Add("code", Secret, XapiScope.All);

        options.Bind(Configuration(new()
        {
            ["Lrs:AuthenticationScheme"] = "",
            ["Lrs:AuthorityHomePage"] = "",
            ["Lrs:MaxStatementsPerPage"] = "",
            ["Lrs:Credentials:0:Key"] = "config",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "all",
        }));

        Assert.Null(options.AuthenticationScheme);
        Assert.Equal(new XapiLrsOptions().AuthorityHomePage, options.AuthorityHomePage);
        Assert.Equal(7, options.MaxStatementsPerPage);
        Assert.Equal(["code", "config"], options.Credentials.Select(c => c.Key));
    }

    [Theory]
    [InlineData("Lrs:Credentials:0:Secret", null, "'Lrs:Credentials:0' has no Secret")]
    [InlineData("Lrs:Credentials:0:Key", null, "'Lrs:Credentials:0' has no Key")]
    [InlineData("Lrs:Credentials:0:Key", "a:b", "'Lrs:Credentials:0' is invalid: A credential key cannot contain a colon")]
    [InlineData("Lrs:Credentials:0:Scopes:0", null, "'Lrs:Credentials:0' has no Scopes")]
    [InlineData("Lrs:Credentials:0:Scopes:0", "statements/writ", "unknown scope 'statements/writ'")]
    // ConfigurationBinder's enum converter would OR these into StatementsRead, and accept numbers
    [InlineData("Lrs:Credentials:0:Scopes:0", "StatementsWrite,StatementsReadMine", "unknown scope")]
    [InlineData("Lrs:Credentials:0:Scopes:0", "8", "unknown scope '8'")]
    [InlineData("Lrs:Credentials:0:Authority:account:name", "lms", "sets Authority")]
    [InlineData("Lrs:Credentials:1:Key", "player", "'Lrs:Credentials:1' is invalid: A credential with key 'player' was already added")]
    [InlineData("Lrs:MaxStatementBytes", "0", "'Lrs:MaxStatementBytes' is out of range")]
    [InlineData("Lrs:MaxStatementsPerPage", "many", "'Lrs:MaxStatementsPerPage' is not a valid Int32")]
    [InlineData("Lrs:EnableVersion1_0_3", "yes", "'Lrs:EnableVersion1_0_3' is not a valid Boolean")]
    [InlineData("Lrs:AuthorityHomePage", "lrs.example.com", "'Lrs:AuthorityHomePage' is not an absolute IRI")]
    public void Bind_rejects_invalid_values_with_the_path_and_without_the_secret(string key, string? value, string expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Lrs:Credentials:0:Key"] = "player",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "all",
        };
        settings[key] = value;
        if (key.StartsWith("Lrs:Credentials:1:", StringComparison.Ordinal))
        {
            settings["Lrs:Credentials:1:Secret"] = Secret;
            settings["Lrs:Credentials:1:Scopes:0"] = "all";
        }

        var exception = Assert.Throws<InvalidOperationException>(() => new XapiLrsOptions().Bind(Configuration(settings)));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credentials_bound_from_configuration_authenticate_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Path.Combine(Path.GetTempPath(), $"xapi-options-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lrs:MigrateOnStartup"] = "true",
            ["Lrs:Credentials:0:Key"] = "reader",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "statements/read",
        });
        builder.Services
            .AddXapiLrs(o => o.Bind(builder.Configuration.GetSection("Lrs")))
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();
        app.MapXapiLrs("/xapi");
        await app.StartAsync(ct);

        try
        {
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Experience-API-Version", "2.0.0");
            client.DefaultRequestHeaders.Authorization = LrsApp.Basic("reader", Secret);

            using var read = await client.GetAsync(new Uri("/xapi/statements", UriKind.Relative), ct);
            using var write = await client.PostAsync(new Uri("/xapi/statements", UriKind.Relative), new StringContent("[]", null, "application/json"), ct);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }
        finally
        {
            await app.StopAsync(ct);
            File.Delete(database);
        }
    }

    private static IConfigurationSection Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection("Lrs");
}
