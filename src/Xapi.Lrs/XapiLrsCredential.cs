namespace Xapi.Lrs;

/// <summary>
/// A key and secret that clients send with HTTP Basic authentication, and the scopes granted to it.
/// </summary>
/// <remarks>
/// The values are checked when the host starts, see <see cref="XapiLrsOptions.Credentials"/>. Configured
/// credentials are then written to the database with the secret hashed (PBKDF2), and the stored list mirrors
/// the configured one. The secret is never stored or logged in plain text.
/// <para>
/// The authority put on the statements a credential stores is an Agent with an account on
/// <see cref="XapiLrsOptions.AuthorityHomePage"/> named after <see cref="Key"/>, unless the credential is
/// created in code with
/// <see cref="XapiLrsCredential(string, string, Agent, string[])"/>. It is not a property, so that the
/// configuration binder, reflection-based or source-generated, can bind this class.
/// </para>
/// </remarks>
public sealed class XapiLrsCredential
{
    // Null for the default account authority. A field rather than a property: the source-generated configuration
    // binder cannot handle a public property of type Agent, and the reflection-based one would silently accept an
    // "Authority" key for a non-public property instead of reporting it as unknown.
    private readonly Agent? _authority;

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

    /// <summary>Creates a credential that stores statements with the given authority.</summary>
    /// <param name="key">The Basic user name.</param>
    /// <param name="secret">The Basic password.</param>
    /// <param name="authority">The authority put on statements stored with this credential.</param>
    /// <param name="scopes">The scopes granted, as <see cref="XapiScopes"/> names.</param>
    public XapiLrsCredential(string key, string secret, Agent authority, params string[] scopes)
        : this(key, secret, scopes)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authority = authority;
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

    internal Agent? GetAuthority() => _authority;
}
