using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>One page of a Statement query: the StatementResult object (IEEE 9274.1.1, 4.1.6.1).</summary>
public sealed record StatementResult
{
    private readonly XapiList<Statement> _statements;
    private readonly XapiList<AttachmentPayload> _attachments = XapiList<AttachmentPayload>.Empty;

    /// <summary>Creates a page.</summary>
    /// <param name="statements">The Statements of this page, possibly empty.</param>
    /// <param name="more">
    /// Where the next page is, as returned by the LRS (usually a relative IRL), or <see langword="null"/> on the
    /// last page.
    /// </param>
    public StatementResult(XapiList<Statement> statements, Uri? more)
    {
        _statements = Guard.NotNull(statements);
        More = more;
    }

    /// <summary>The Statements of this page, possibly empty.</summary>
    public XapiList<Statement> Statements
    {
        get => _statements;
        init => _statements = Guard.NotNull(value);
    }

    /// <summary>
    /// Where the next page is, as returned by the LRS (usually a relative IRL), or <see langword="null"/> on the last
    /// page. Pass it to <see cref="XapiClient.GetMoreStatementsAsync"/>.
    /// </summary>
    public Uri? More { get; init; }

    /// <summary>
    /// The <c>X-Experience-API-Consistent-Through</c> response header: Statements stored before this time are known
    /// to be available for retrieval.
    /// </summary>
    public DateTimeOffset? ConsistentThrough { get; init; }

    /// <summary>
    /// Attachment data returned for <see cref="StatementQuery.Attachments"/>, matched to the Statements' Attachment
    /// objects by <see cref="AttachmentPayload.Sha2"/>. Empty otherwise.
    /// </summary>
    public XapiList<AttachmentPayload> Attachments
    {
        get => _attachments;
        init => _attachments = Guard.NotNull(value);
    }
}
