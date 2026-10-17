using System.Security.Claims;

namespace Xapi.Lrs;

/// <summary>Options for the LRS added with <c>AddXapiLrs</c>.</summary>
/// <remarks>
/// The options can be bound from a configuration section, such as <c>Lrs</c> in <c>appsettings.json</c>, with
/// the <c>AddXapiLrs</c> overload that takes an <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// or with <c>Configure&lt;XapiLrsOptions&gt;(section)</c>. Setters do not check values; the options are
/// validated when the host starts, and invalid ones stop it with an
/// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> that lists every problem.
/// </remarks>
public sealed class XapiLrsOptions
{
    /// <summary>
    /// The credentials accepted with HTTP Basic authentication. Ignored when <see cref="AuthenticationScheme"/>
    /// is set.
    /// </summary>
    /// <remarks>
    /// In configuration, each credential has a <c>Key</c>, a <c>Secret</c> and a <c>Scopes</c> array of xAPI scope
    /// names, for example <c>Lrs:Credentials:0:Key</c>, <c>Lrs:Credentials:0:Secret</c> and
    /// <c>Lrs:Credentials:0:Scopes:0</c>. Startup fails when a key is missing, empty, contains a colon or appears
    /// twice, when a secret is missing or empty, or when a credential has no scope or an unknown one. The messages
    /// name the credential by its index and key, never by its secret.
    /// </remarks>
    public XapiLrsCredentialList Credentials { get; } = new();

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
    /// statement and are not rewritten later. Must be absolute.
    /// </summary>
    /// <remarks>
    /// The text the URI was created from is used as is, without the normalization of <see cref="Uri.AbsoluteUri"/>.
    /// </remarks>
    public Uri AuthorityHomePage { get; set; } = new("https://xapi-dotnet.invalid/credentials");

    /// <summary>
    /// Whether requests with an <c>X-Experience-API-Version</c> header of <c>1.0.x</c> are accepted and
    /// handled with xAPI 1.0.3 rules. When <see langword="false"/>, only 2.0 requests are accepted.
    /// </summary>
    public bool EnableVersion1_0_3 { get; set; } = true;

    /// <summary>
    /// The largest State, Activity Profile or Agent Profile document accepted, in bytes; larger ones get 413
    /// (IEEE 9274.1.1, 4.1.5). Applies to the document stored after a POST merge as well. Defaults to 1 MiB, and
    /// must be at least 1. The server's request body limit (<c>MaxRequestBodySize</c>) of document writes is set
    /// to this value, so it replaces Kestrel's default of about 28.6 MiB on those requests.
    /// </summary>
    public long MaxDocumentBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// The most statement JSON accepted in one statement PUT or POST, in bytes: the whole body of an
    /// <c>application/json</c> request, or the first part of a <c>multipart/mixed</c> one. A batch counts as a
    /// whole. Larger requests get 413 (IEEE 9274.1.1, 4.1.5). Defaults to 16 MiB, and
    /// must be at least 1.
    /// </summary>
    /// <remarks>
    /// The server's request body limit (<c>MaxRequestBodySize</c>) of statement writes is set from this value
    /// and <see cref="MaxAttachmentBytes"/>, so these options replace Kestrel's default of about 28.6 MiB on
    /// those requests. Servers without a per-request limit, and requests whose body was already read by other
    /// middleware, keep the server's own limit; the LRS still enforces these options while reading.
    /// </remarks>
    public long MaxStatementBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// The most attachment data accepted in one <c>multipart/mixed</c> statement request, in bytes, counting
    /// every attachment part; larger requests get 413 (IEEE 9274.1.1, 4.1.5). Defaults to 50 MiB, and must not
    /// be negative. The server's request body limit of a <c>multipart/mixed</c> statement request is set to this
    /// value plus <see cref="MaxStatementBytes"/> and a fixed 1 MiB for part headers and boundaries.
    /// </summary>
    public long MaxAttachmentBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// The most statements accepted in one statement POST; larger batches get 413 (IEEE 9274.1.1, 4.1.5) before
    /// they are parsed. Defaults to 1,000, and must be at least 1.
    /// </summary>
    /// <remarks>
    /// A batch is parsed in memory and stored in one transaction, so this bounds what one request costs whatever
    /// its byte size: parsing takes about 20 times the JSON's size, and on SQLite every other write waits until
    /// the batch is stored. A batch of 1,000 statements of 1 KB takes about 20 MB to parse and 0.3 to 0.5 seconds
    /// to store (docs/benchmarks.md). Clients that send larger batches must split them.
    /// </remarks>
    public int MaxStatementsPerBatch { get; set; } = 1000;

    /// <summary>
    /// The largest form body of an xAPI 1.0.3 Alternate Request Syntax request (a POST with a <c>method</c>
    /// query parameter), in bytes, counting every URL-encoded field. Larger requests get 413. Defaults to 1 MiB,
    /// and must be at least 1.
    /// </summary>
    /// <remarks>
    /// The LRS reads this form before it authenticates the request, because the credentials can be one of its
    /// fields. This limit, rather than <see cref="MaxStatementBytes"/> or the server's request body limit, is
    /// what an unauthenticated client can make the LRS read and hold in memory. The statements or document in
    /// the form must also fit <see cref="MaxStatementBytes"/> or <see cref="MaxDocumentBytes"/>.
    /// </remarks>
    public long MaxAlternateRequestBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// The most statements one page of a statement query returns, and the page size when the request asks for
    /// <c>limit=0</c> or gives no limit. Larger limits are lowered to it. Defaults to 100, and
    /// must be at least 1.
    /// </summary>
    public int MaxStatementsPerPage { get; set; } = 100;

    /// <summary>
    /// Whether the database migrations are applied when the host starts. Off by default, so production
    /// deployments can migrate as a separate step with <c>MigrateXapiLrsDatabaseAsync</c>.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
