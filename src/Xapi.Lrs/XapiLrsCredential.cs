namespace Xapi.Lrs;

/// <summary>
/// A key and secret that clients send with HTTP Basic authentication, and the scopes granted to it.
/// </summary>
/// <remarks>
/// The values are checked when the host starts, see <see cref="XapiLrsOptions.Credentials"/>. Configured
/// credentials are then written to the database with the secret hashed (PBKDF2), and the stored list mirrors
/// the configured one. The secret is never stored or logged in plain text.
/// </remarks>
public sealed class XapiLrsCredential
{
    /// <summary>Creates an empty credential, for example for the configuration binder.</summary>
    public XapiLrsCredential()
    {
    }

    /// <summary>Creates a credential.</summary>
    /// <param name="key">The Basic user name.</param>
    /// <param name="secret">The Basic password.</param>
    /// <param name="scopes">The scopes granted, as <see cref="XapiScopes"/> names.</param>
    public XapiLrsCredential(string key, string secret, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        Key = key;
        Secret = secret;
        foreach (var scope in scopes)
        {
            Scopes.Add(scope);
        }
    }

    /// <summary>The Basic user name. Required; must not be empty or contain a colon.</summary>
    public string? Key { get; set; }

    /// <summary>The Basic password. Required; must not be empty.</summary>
    public string? Secret { get; set; }

    /// <summary>
    /// The scopes granted, as xAPI scope names such as <see cref="XapiScopes.StatementsWrite"/>
    /// (<c>statements/write</c>). At least one is required.
    /// </summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>
    /// The authority put on statements stored with this credential. When <see langword="null"/>, an Agent with
    /// an account on <see cref="XapiLrsOptions.AuthorityHomePage"/> named after <see cref="Key"/> is used.
    /// Cannot be read from configuration; set it in code.
    /// </summary>
    public Agent? Authority { get; set; }
}
