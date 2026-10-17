using Microsoft.Extensions.Options;
using Xapi.Lrs.Security;

namespace Xapi.Lrs.Hosting;

// Checks every option, since the setters do not: the configuration binder sets them one by one, and a value can
// only be judged once all are set. Registered with ValidateOnStart, so the host fails to start instead of the first
// request failing, and every problem is listed at once. Credentials are named by index and key; a message never
// contains a secret.
internal sealed class XapiLrsOptionsValidator : IValidateOptions<XapiLrsOptions>
{
    public ValidateOptionsResult Validate(string? name, XapiLrsOptions options)
    {
        var failures = new List<string>();
        var scheme = options.AuthenticationScheme;
        if (scheme is null)
        {
            if (options.Credentials.Count == 0)
            {
                failures.Add($"No request can be authenticated: add a credential to {nameof(XapiLrsOptions.Credentials)}, or set {nameof(XapiLrsOptions.AuthenticationScheme)} to authenticate with another scheme.");
            }
        }
        else if (string.IsNullOrWhiteSpace(scheme))
        {
            failures.Add($"{nameof(XapiLrsOptions.AuthenticationScheme)} is empty. Leave it null to use the Basic credentials in {nameof(XapiLrsOptions.Credentials)}.");
        }
        else if (string.Equals(scheme, XapiLrsAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            // The configured credentials would not be stored, and the Basic handler would check stale ones
            failures.Add($"{nameof(XapiLrsOptions.AuthenticationScheme)} is the built-in Basic scheme '{scheme}'. Leave it null to use the Basic credentials in {nameof(XapiLrsOptions.Credentials)}.");
        }

        if (string.IsNullOrWhiteSpace(options.ScopeClaimType))
        {
            failures.Add($"{nameof(XapiLrsOptions.ScopeClaimType)} is empty.");
        }

        if (options.AuthorityHomePage is not { } homePage)
        {
            // The configuration binder sets a Uri to null for an empty value, such as an empty environment variable
            failures.Add($"{nameof(XapiLrsOptions.AuthorityHomePage)} is null or empty. Set it to an absolute URI, such as the LRS's own address.");
        }
        else if (!homePage.IsAbsoluteUri || !Iri.TryParse(homePage.OriginalString, out _))
        {
            failures.Add($"{nameof(XapiLrsOptions.AuthorityHomePage)} '{homePage.OriginalString}' is not an absolute URI.");
        }

        AtLeast(failures, nameof(XapiLrsOptions.MaxDocumentBytes), options.MaxDocumentBytes, 1);
        AtLeast(failures, nameof(XapiLrsOptions.MaxStatementBytes), options.MaxStatementBytes, 1);
        AtLeast(failures, nameof(XapiLrsOptions.MaxAttachmentBytes), options.MaxAttachmentBytes, 0);
        AtLeast(failures, nameof(XapiLrsOptions.MaxStatementsPerBatch), options.MaxStatementsPerBatch, 1);
        AtLeast(failures, nameof(XapiLrsOptions.MaxAlternateRequestBytes), options.MaxAlternateRequestBytes, 1);
        AtLeast(failures, nameof(XapiLrsOptions.MaxStatementsPerPage), options.MaxStatementsPerPage, 1);

        ValidateCredentials(failures, options.Credentials);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures.Select(f => "xAPI LRS options: " + f));
    }

    private static void AtLeast(List<string> failures, string option, long value, long minimum)
    {
        if (value < minimum)
        {
            failures.Add($"{option} is {value}; it must be at least {minimum}.");
        }
    }

    private static void ValidateCredentials(List<string> failures, XapiLrsCredentialList credentials)
    {
        var firstIndexOfKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < credentials.Count; i++)
        {
            var credential = credentials[i];
            var key = credential.Key;
            var name = string.IsNullOrEmpty(key) ? $"Credentials[{i}]" : $"Credentials[{i}] ('{key}')";
            if (string.IsNullOrEmpty(key))
            {
                failures.Add($"{name} has no {nameof(XapiLrsCredential.Key)}.");
            }
            else if (key.Contains(':', StringComparison.Ordinal))
            {
                // RFC 7617, 2: the user-id cannot contain a colon, the first colon separates it from the password
                failures.Add($"{name} has a {nameof(XapiLrsCredential.Key)} with a colon, which Basic authentication cannot send.");
            }
            else if (!firstIndexOfKey.TryAdd(key, i))
            {
                failures.Add($"{name} has the same {nameof(XapiLrsCredential.Key)} as Credentials[{firstIndexOfKey[key]}].");
            }

            if (string.IsNullOrEmpty(credential.Secret))
            {
                failures.Add($"{name} has no {nameof(XapiLrsCredential.Secret)}.");
            }

            if (credential.Scopes.Count == 0)
            {
                failures.Add($"{name} has no {nameof(XapiLrsCredential.Scopes)}. Give at least one, such as {XapiScopes.StatementsWrite}; in configuration, as an array (Scopes:0, Scopes:1).");
            }

            foreach (var scope in credential.Scopes)
            {
                // Exact names only: no enum names, numbers, or several names in one entry
                if (scope is null || Scopes.Parse(scope) == ScopeSet.None)
                {
                    failures.Add($"{name} has an unknown scope '{scope}'. Use one xAPI scope name per entry, such as {XapiScopes.StatementsWrite} (see {nameof(XapiScopes)}).");
                }
            }
        }
    }
}
