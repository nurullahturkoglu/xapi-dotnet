using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// A statement ready to be stored: every property the LRS sets (<c>id</c>, <c>stored</c>, <c>timestamp</c>,
/// <c>authority</c>, <c>version</c>) is filled in, and its storage key is assigned.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class StatementRecord
{
    /// <summary>Creates a record.</summary>
    /// <param name="key">The storage key, see <see cref="Key"/>.</param>
    /// <param name="statement">The prepared statement. Its <see cref="Statement.Id"/> and <see cref="Statement.Stored"/> must be set.</param>
    /// <param name="json">The statement as returned to clients in the <c>exact</c> format.</param>
    /// <param name="authorityKey">An identifier of the authority, see <see cref="AuthorityKey"/>.</param>
    /// <exception cref="ArgumentException">The statement has no id or stored time.</exception>
    public StatementRecord(Guid key, Statement statement, string json, string authorityKey)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentException.ThrowIfNullOrEmpty(json);
        ArgumentException.ThrowIfNullOrEmpty(authorityKey);
        if (statement.Id is null || statement.Stored is null)
        {
            throw new ArgumentException("The statement must have an id and a stored time.", nameof(statement));
        }

        Key = key;
        Statement = statement;
        Json = json;
        AuthorityKey = authorityKey;
    }

    /// <summary>
    /// A monotonic UUIDv7 whose time part is the statement's <c>stored</c> time. It orders statements by
    /// stored time and serves as the paging cursor (ADR-020).
    /// </summary>
    public Guid Key { get; }

    /// <summary>The prepared statement.</summary>
    public Statement Statement { get; }

    /// <summary>The statement id.</summary>
    public Guid Id => Statement.Id!.Value;

    /// <summary>The statement as returned to clients in the <c>exact</c> format.</summary>
    public string Json { get; }

    /// <summary>
    /// An identifier of the statement's authority, used to find "my" statements for the
    /// <c>statements/read/mine</c> scope.
    /// </summary>
    public string AuthorityKey { get; }

    /// <summary>
    /// The attachment data received with the statement, for the Attachment objects that have it. Data shared by
    /// several statements of a batch is the same instance in each record and is stored once.
    /// </summary>
    public IReadOnlyList<AttachmentContent> Attachments
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = [];
}
