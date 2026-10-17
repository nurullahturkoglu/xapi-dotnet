using Microsoft.Extensions.Options;

namespace Xapi.Lrs.Hosting;

// Checks what the setters cannot: options that are only wrong together, and values set to something the property
// type allows but the LRS cannot use. Registered with ValidateOnStart, so the host fails to start instead of the
// first request failing.
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

        if (options.AuthorityHomePage.IsEmpty)
        {
            failures.Add($"{nameof(XapiLrsOptions.AuthorityHomePage)} is the default Iri, which is not an IRI.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures.Select(f => "xAPI LRS options: " + f));
    }
}
