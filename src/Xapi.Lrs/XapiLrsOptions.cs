using System.Security.Claims;

namespace Xapi.Lrs;

/// <summary>Options for the LRS added with <c>AddXapiLrs</c>.</summary>
public sealed class XapiLrsOptions
{
    /// <summary>
    /// The credentials accepted with HTTP Basic authentication. Ignored when <see cref="AuthenticationScheme"/>
    /// is set.
    /// </summary>
    public XapiCredentialList Credentials { get; } = new();

    /// <summary>
    /// An ASP.NET Core authentication scheme to authenticate requests with instead of the built-in Basic
    /// credentials, for example <c>"Bearer"</c>. Scopes are read from <see cref="ScopeClaimType"/> claims and
    /// the authority comes from <see cref="AuthorityFactory"/>.
    /// </summary>
    public string? AuthenticationScheme { get; set; }

    /// <summary>
    /// The claim type that carries scopes for <see cref="AuthenticationScheme"/>, as space-separated xAPI scope
    /// names such as <c>statements/write statements/read</c>. Defaults to <c>scope</c>.
    /// </summary>
    public string ScopeClaimType { get; set; } = "scope";

    /// <summary>
    /// Maps a principal authenticated with <see cref="AuthenticationScheme"/> to the authority put on the
    /// statements it stores. When <see langword="null"/>, an Agent with an account on
    /// <see cref="AuthorityHomePage"/> named after the principal's name identifier is used.
    /// </summary>
    public Func<ClaimsPrincipal, Agent>? AuthorityFactory { get; set; }

    /// <summary>
    /// The <c>homePage</c> of the account in generated authorities. Set it to a URL you control, such as the
    /// LRS's own address, before storing statements you intend to keep: authorities are stored with each
    /// statement and are not rewritten later.
    /// </summary>
    public Iri AuthorityHomePage { get; set; } = new("https://xapi-dotnet.invalid/credentials");

    /// <summary>
    /// Whether requests with an <c>X-Experience-API-Version</c> header of <c>1.0.x</c> are accepted and
    /// handled with xAPI 1.0.3 rules. When <see langword="false"/>, only 2.0 requests are accepted.
    /// </summary>
    public bool EnableVersion1_0_3 { get; set; } = true;

    /// <summary>
    /// The largest State, Activity Profile or Agent Profile document accepted, in bytes; larger ones get 413
    /// (IEEE 9274.1.1, 4.1.5). Applies to the document stored after a POST merge as well. Defaults to 1 MiB.
    /// </summary>
    public long MaxDocumentBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// The most statements one page of a statement query returns, and the page size when the request asks for
    /// <c>limit=0</c> or gives no limit. Larger limits are lowered to it. Defaults to 100.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxStatementsPerPage
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 100;

    /// <summary>
    /// Whether the database migrations are applied when the host starts. Off by default, so production
    /// deployments can migrate as a separate step.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
