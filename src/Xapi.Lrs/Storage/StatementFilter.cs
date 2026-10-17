using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// The filters, order and page size of a statement query (GET on the Statement Resource without
/// <c>statementId</c>, IEEE 9274.1.1, 4.1.6.1). Voided statements are never returned.
/// </summary>
/// <remarks>
/// The content filters (<see cref="Agent"/>, <see cref="Verb"/>, <see cref="Activity"/>,
/// <see cref="Registration"/>) also match a statement whose object is a StatementRef to a matching statement,
/// directly or through further StatementRefs, even when that target is voided (4.1.6.1 "Filter Conditions for
/// StatementRefs"). The time and paging bounds apply to the returned statement itself.
/// </remarks>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed record StatementFilter
{
    /// <summary>
    /// Only statements whose actor or object is this Agent or identified Group, or a Group with this Agent as a
    /// member. Matched by Inverse Functional Identifier.
    /// </summary>
    public Actor? Agent { get; init; }

    /// <summary>Only statements with this verb id.</summary>
    public Iri? Verb { get; init; }

    /// <summary>Only statements whose object is the Activity with this id.</summary>
    public Iri? Activity { get; init; }

    /// <summary>Only statements with this registration.</summary>
    public Guid? Registration { get; init; }

    /// <summary>
    /// Also match <see cref="Activity"/> in context Activities and in a SubStatement's object and context.
    /// </summary>
    public bool RelatedActivities { get; init; }

    /// <summary>
    /// Also match <see cref="Agent"/> as authority, instructor, team, context Agent or Group, and in a
    /// SubStatement.
    /// </summary>
    public bool RelatedAgents { get; init; }

    /// <summary>Only statements stored after this time (exclusive).</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Only statements stored at or before this time.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>Return the oldest statements first instead of the newest.</summary>
    public bool Ascending { get; init; }

    /// <summary>The maximum number of statements in the page. Must be at least 1.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Continue after the previous page of the same query: its <see cref="StatementPage.ContinuationToken"/>, or
    /// <see langword="null"/> for the first page. The token reaches the store from a client, so a store rejects
    /// one it cannot read (see <see cref="IStatementStore.QueryAsync"/>).
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Only statements stored with this authority, see <see cref="StatementRecord.AuthorityKey"/>. Used for the
    /// <c>statements/read/mine</c> scope.
    /// </summary>
    public string? AuthorityKey { get; init; }
}
