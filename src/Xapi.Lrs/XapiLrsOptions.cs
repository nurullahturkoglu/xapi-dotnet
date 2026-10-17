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
    /// The server's request body limit (<c>MaxRequestBodySize</c>) of document writes is set to this value, so
    /// it replaces Kestrel's default of about 28.6 MiB on those requests.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public long MaxDocumentBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1024 * 1024;

    /// <summary>
    /// The most statement JSON accepted in one statement PUT or POST, in bytes: the whole body of an
    /// <c>application/json</c> request, or the first part of a <c>multipart/mixed</c> one. A batch counts as a
    /// whole. Larger requests get 413 (IEEE 9274.1.1, 4.1.5). Defaults to 16 MiB.
    /// </summary>
    /// <remarks>
    /// The server's request body limit (<c>MaxRequestBodySize</c>) of statement writes is set from this value
    /// and <see cref="MaxAttachmentBytes"/>, so these options replace Kestrel's default of about 28.6 MiB on
    /// those requests. Servers without a per-request limit, and requests whose body was already read by other
    /// middleware, keep the server's own limit; the LRS still enforces these options while reading.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public long MaxStatementBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 16 * 1024 * 1024;

    /// <summary>
    /// The most attachment data accepted in one <c>multipart/mixed</c> statement request, in bytes, counting
    /// every attachment part; larger requests get 413 (IEEE 9274.1.1, 4.1.5). Defaults to 50 MiB. The
    /// server's request body limit of a <c>multipart/mixed</c> statement request is set to this value plus
    /// <see cref="MaxStatementBytes"/> and a fixed 1 MiB for part headers and boundaries.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MaxAttachmentBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 50 * 1024 * 1024;

    /// <summary>
    /// The most statements accepted in one statement POST; larger batches get 413 (IEEE 9274.1.1, 4.1.5) before
    /// they are parsed. Defaults to 1,000.
    /// </summary>
    /// <remarks>
    /// A batch is parsed in memory and stored in one transaction, so this bounds what one request costs whatever
    /// its byte size: parsing takes about 20 times the JSON's size, and on SQLite every other write waits until
    /// the batch is stored. A batch of 1,000 statements of 1 KB takes about 20 MB to parse and 0.3 to 0.5 seconds
    /// to store (docs/benchmarks.md). Clients that send larger batches must split them.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxStatementsPerBatch
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1000;

    /// <summary>
    /// The largest form body of an xAPI 1.0.3 Alternate Request Syntax request (a POST with a <c>method</c>
    /// query parameter), in bytes, counting every URL-encoded field. Larger requests get 413. Defaults to 1 MiB.
    /// </summary>
    /// <remarks>
    /// The LRS reads this form before it authenticates the request, because the credentials can be one of its
    /// fields. This limit, rather than <see cref="MaxStatementBytes"/> or the server's request body limit, is
    /// what an unauthenticated client can make the LRS read and hold in memory. The statements or document in
    /// the form must also fit <see cref="MaxStatementBytes"/> or <see cref="MaxDocumentBytes"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public long MaxAlternateRequestBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1024 * 1024;

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
    /// deployments can migrate as a separate step with <c>MigrateXapiLrsDatabaseAsync</c>.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
