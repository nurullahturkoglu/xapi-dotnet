using System.Globalization;
using Microsoft.Extensions.Configuration;
using Xapi.Lrs.Security;

namespace Xapi.Lrs.Hosting;

// XapiLrsOptions.Bind. ConfigurationBinder is not used: it skips a collection element it cannot create without an
// error, so a credential with a missing secret or a misspelt scope would silently not exist, and its enum converter
// ORs comma-separated names, so "StatementsWrite,StatementsReadMine" would grant StatementsRead.
internal static class XapiLrsConfigurationReader
{
    public static void Read(XapiLrsOptions options, IConfiguration configuration)
    {
        // Empty values are skipped, so that an empty environment variable falls back on the default
        if (configuration[nameof(XapiLrsOptions.AuthenticationScheme)] is { Length: > 0 } scheme)
        {
            options.AuthenticationScheme = scheme;
        }

        if (configuration[nameof(XapiLrsOptions.ScopeClaimType)] is { Length: > 0 } claimType)
        {
            options.ScopeClaimType = claimType;
        }

        var homePage = configuration.GetSection(nameof(XapiLrsOptions.AuthorityHomePage));
        if (homePage.Value is { Length: > 0 } homePageText)
        {
            options.AuthorityHomePage = Iri.TryParse(homePageText, out var iri)
                ? iri
                : throw new InvalidOperationException($"The configuration value at '{homePage.Path}' is not an absolute IRI.");
        }

        Set(configuration, nameof(XapiLrsOptions.EnableVersion1_0_3), ParseBoolean, value => options.EnableVersion1_0_3 = value);
        Set(configuration, nameof(XapiLrsOptions.MaxDocumentBytes), ParseInt64, value => options.MaxDocumentBytes = value);
        Set(configuration, nameof(XapiLrsOptions.MaxStatementBytes), ParseInt64, value => options.MaxStatementBytes = value);
        Set(configuration, nameof(XapiLrsOptions.MaxAttachmentBytes), ParseInt64, value => options.MaxAttachmentBytes = value);
        Set(configuration, nameof(XapiLrsOptions.MaxStatementsPerBatch), ParseInt32, value => options.MaxStatementsPerBatch = value);
        Set(configuration, nameof(XapiLrsOptions.MaxAlternateRequestBytes), ParseInt64, value => options.MaxAlternateRequestBytes = value);
        Set(configuration, nameof(XapiLrsOptions.MaxStatementsPerPage), ParseInt32, value => options.MaxStatementsPerPage = value);
        Set(configuration, nameof(XapiLrsOptions.MigrateOnStartup), ParseBoolean, value => options.MigrateOnStartup = value);

        foreach (var section in configuration.GetSection(nameof(XapiLrsOptions.Credentials)).GetChildren())
        {
            try
            {
                options.Credentials.Add(ReadCredential(section));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"The credential at '{section.Path}' is invalid: {exception.Message}", exception);
            }
        }
    }

    // Messages name the configuration path and never contain the secret
    private static XapiCredential ReadCredential(IConfigurationSection section)
    {
        var key = section[nameof(XapiCredential.Key)];
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException($"The credential at '{section.Path}' has no {nameof(XapiCredential.Key)}.");
        }

        var secret = section["Secret"];
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException($"The credential at '{section.Path}' has no Secret.");
        }

        if (section.GetSection(nameof(XapiCredential.Authority)).Exists())
        {
            throw new InvalidOperationException($"The credential at '{section.Path}' sets {nameof(XapiCredential.Authority)}, which cannot be read from configuration. Add this credential in code instead.");
        }

        var scopesSection = section.GetSection(nameof(XapiCredential.Scopes));
        var names = scopesSection.GetChildren().Select(c => c.Value).Append(scopesSection.Value)
            .SelectMany(value => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var scopes = new List<XapiScope>();
        foreach (var name in names)
        {
            scopes.Add(ParseScope(name)
                ?? throw new InvalidOperationException($"The credential at '{section.Path}' has an unknown scope '{name}'. Use an xAPI scope name such as statements/write, or an {nameof(XapiScope)} name such as StatementsWrite."));
        }

        if (scopes.Count == 0)
        {
            throw new InvalidOperationException($"The credential at '{section.Path}' has no {nameof(XapiCredential.Scopes)}. Give at least one.");
        }

        return new XapiCredential(key, secret, scopes);
    }

    // Exact names only: no numbers and no comma-separated combinations
    private static XapiScope? ParseScope(string name)
    {
        foreach (var scope in Enum.GetValues<XapiScope>())
        {
            if (string.Equals(name, Scopes.Name(scope), StringComparison.Ordinal)
                || string.Equals(name, scope.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return scope;
            }
        }

        return null;
    }

    private static void Set<T>(IConfiguration configuration, string name, Func<string, T?> parse, Action<T> set)
        where T : struct
    {
        var section = configuration.GetSection(name);
        if (section.Value is not { Length: > 0 } text)
        {
            return;
        }

        var value = parse(text) ?? throw new InvalidOperationException($"The configuration value at '{section.Path}' is not a valid {typeof(T).Name}.");
        try
        {
            set(value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException($"The configuration value at '{section.Path}' is out of range: {exception.Message}", exception);
        }
    }

    private static bool? ParseBoolean(string text) => bool.TryParse(text, out var value) ? value : null;

    private static int? ParseInt32(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? ParseInt64(string text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
