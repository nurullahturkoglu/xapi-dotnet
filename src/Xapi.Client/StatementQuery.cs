using Xapi.Json;

namespace Xapi.Client;

/// <summary>
/// Filters and options for a Statement query (GET on the Statement resource without <c>statementId</c>),
/// IEEE 9274.1.1, 4.1.6.1. Properties left at their defaults are not sent.
/// </summary>
public sealed record StatementQuery
{
    /// <summary>
    /// Only Statements whose Actor or Object is this Agent or identified Group, or a Group with this Agent as a
    /// member (<c>agent</c>).
    /// </summary>
    public Actor? Agent { get; init; }

    /// <summary>Only Statements with this Verb id (<c>verb</c>).</summary>
    public Iri? Verb { get; init; }

    /// <summary>Only Statements whose Object is the Activity with this id (<c>activity</c>).</summary>
    public Iri? Activity { get; init; }

    /// <summary>Only Statements with this registration (<c>registration</c>).</summary>
    public Guid? Registration { get; init; }

    /// <summary>
    /// Also match <see cref="Activity"/> in context Activities and SubStatements (<c>related_activities</c>).
    /// </summary>
    public bool RelatedActivities { get; init; }

    /// <summary>
    /// Also match <see cref="Agent"/> as authority, instructor, team, context Agent or Group, and in SubStatements
    /// (<c>related_agents</c>).
    /// </summary>
    public bool RelatedAgents { get; init; }

    /// <summary>Only Statements stored after this time, exclusive (<c>since</c>).</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Only Statements stored at or before this time (<c>until</c>).</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// The page size (<c>limit</c>). <see langword="null"/> or 0 lets the LRS choose its maximum. Further pages are
    /// fetched through <see cref="StatementResult.More"/>.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>How much of Agents, Activities and Verbs the LRS returns (<c>format</c>).</summary>
    public StatementFormat Format { get; init; }

    /// <summary>Return the oldest Statements first (<c>ascending</c>).</summary>
    public bool Ascending { get; init; }

    /// <summary>
    /// Also return attachment data, in a <c>multipart/mixed</c> response (<c>attachments</c>). The data ends up in
    /// <see cref="StatementResult.Attachments"/>.
    /// </summary>
    public bool Attachments { get; init; }
}
