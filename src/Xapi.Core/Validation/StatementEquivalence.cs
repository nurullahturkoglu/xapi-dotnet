using Xapi.Internal;

namespace Xapi.Validation;

/// <summary>
/// Decides whether two Statements are the same Statement under the standard's immutability rules, for example
/// when an LRS receives a Statement id it already stores (409 Conflict) or a client checks what it sent.
/// </summary>
/// <remarks>
/// <para>
/// IEEE 9274.1.1, 4.2 "Statement Immutability" (and 5.2 in the Content book): the Actor (except member order),
/// the Verb (except <c>display</c>), the object and the duration are immutable; case, id, the order of Group
/// members, authority, stored, timestamp, version, attachments and referenced Activity definitions are not
/// considered. xAPI 1.0.3 Data 2.3.1 says the same and adds that differences outside those exceptions must not
/// be ignored, so <c>result</c> and <c>context</c> are compared too.
/// </para>
/// <para>What this means here:</para>
/// <list type="bullet">
/// <item><description>Ignored: <c>id</c>, <c>stored</c>, <c>authority</c>, <c>version</c>, the Statement <c>timestamp</c>, <c>attachments</c> (also inside SubStatements), Verb <c>display</c>, and every Activity <c>definition</c> (object, SubStatement and context Activities).</description></item>
/// <item><description>Strings, IRIs, names, identifiers, language tags and extension keys and string values compare ignoring case ("Case (upper vs. lower)" is listed as not considered).</description></item>
/// <item><description>Group <c>member</c> lists compare as unordered collections; all other lists keep their order.</description></item>
/// <item><description><c>result.duration</c> compares to 0.01 second (4.2.7 "Duration"); see <see cref="XapiDuration"/>.</description></item>
/// <item><description>A SubStatement's <c>timestamp</c> is part of the object, so it is compared, as a UTC instant at millisecond precision (4.2.7 allows an LRS to keep only 3 fraction digits).</description></item>
/// <item><description>Extension values compare deeply (object property order ignored, numbers by value).</description></item>
/// </list>
/// </remarks>
public static class StatementEquivalence
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    /// <summary>Returns whether two Statements are equivalent under the immutability rules described above.</summary>
    /// <param name="a">The first Statement.</param>
    /// <param name="b">The second Statement.</param>
    /// <returns><see langword="true"/> when the Statements are equivalent.</returns>
    public static bool AreEquivalent(Statement a, Statement b)
    {
        Guard.NotNull(a);
        Guard.NotNull(b);
        return ActorEquals(a.Actor, b.Actor) &&
               IriEquals(a.Verb.Id, b.Verb.Id) &&
               ObjectEquals(a.Object, b.Object) &&
               ResultEquals(a.Result, b.Result) &&
               ContextEquals(a.Context, b.Context);
    }

    private static bool ObjectEquals(StatementObject a, StatementObject b) =>
        (a, b) switch
        {
            (Activity x, Activity y) => IriEquals(x.Id, y.Id),
            (Actor x, Actor y) => ActorEquals(x, y),
            (StatementRef x, StatementRef y) => x.Id == y.Id,
            (SubStatement x, SubStatement y) => SubStatementEquals(x, y),
            _ => false,
        };

    private static bool SubStatementEquals(SubStatement a, SubStatement b) =>
        ActorEquals(a.Actor, b.Actor) &&
        IriEquals(a.Verb.Id, b.Verb.Id) &&
        ObjectEquals(a.Object, b.Object) &&
        ResultEquals(a.Result, b.Result) &&
        ContextEquals(a.Context, b.Context) &&
        Nullable.Equals(TruncateToMilliseconds(a.Timestamp), TruncateToMilliseconds(b.Timestamp));

    private static bool ActorEquals(Actor? a, Actor? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (!StringEquals(a.Name, b.Name))
        {
            return false;
        }

        return (a, b) switch
        {
            (Agent x, Agent y) => IfiEquals(x.Ifi, y.Ifi),
            (Group x, Group y) => IfiEquals(x.Ifi, y.Ifi) && UnorderedEquals(x.Members, y.Members, (m, n) => ActorEquals(m, n)),
            _ => false,
        };
    }

    private static bool IfiEquals(InverseFunctionalIdentifier? a, InverseFunctionalIdentifier? b) =>
        (a, b) switch
        {
            (null, null) => true,
            (Mbox x, Mbox y) => StringEquals(x.Value, y.Value),
            (MboxSha1Sum x, MboxSha1Sum y) => StringEquals(x.Value, y.Value),
            (OpenId x, OpenId y) => IriEquals(x.Value, y.Value),
            (Account x, Account y) => IriEquals(x.HomePage, y.HomePage) && StringEquals(x.Name, y.Name),
            _ => false,
        };

    private static bool ResultEquals(Result? a, Result? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return ScoreEquals(a.Score, b.Score) &&
               a.Success == b.Success &&
               a.Completion == b.Completion &&
               StringEquals(a.Response, b.Response) &&
               Nullable.Equals(a.Duration, b.Duration) &&
               ExtensionsEquals(a.Extensions, b.Extensions);
    }

    private static bool ScoreEquals(Score? a, Score? b) =>
        a is null || b is null
            ? a is null && b is null
            : Nullable.Equals(a.Scaled, b.Scaled) && Nullable.Equals(a.Raw, b.Raw) && Nullable.Equals(a.Min, b.Min) && Nullable.Equals(a.Max, b.Max);

    private static bool ContextEquals(Context? a, Context? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Registration == b.Registration &&
               ActorEquals(a.Instructor, b.Instructor) &&
               ActorEquals(a.Team, b.Team) &&
               OrderedEquals(a.ContextAgents, b.ContextAgents, (x, y) => ActorEquals(x.Agent, y.Agent) && OrderedEquals(x.RelevantTypes, y.RelevantTypes, IriEquals)) &&
               OrderedEquals(a.ContextGroups, b.ContextGroups, (x, y) => ActorEquals(x.Group, y.Group) && OrderedEquals(x.RelevantTypes, y.RelevantTypes, IriEquals)) &&
               ContextActivitiesEquals(a.ContextActivities, b.ContextActivities) &&
               StringEquals(a.Revision, b.Revision) &&
               StringEquals(a.Platform, b.Platform) &&
               StringEquals(a.Language, b.Language) &&
               a.Statement?.Id == b.Statement?.Id &&
               ExtensionsEquals(a.Extensions, b.Extensions);
    }

    private static bool ContextActivitiesEquals(ContextActivities? a, ContextActivities? b)
    {
        var empty = new ContextActivities();
        a ??= empty;
        b ??= empty;
        return ActivityIdsEqual(a.Parent, b.Parent) &&
               ActivityIdsEqual(a.Grouping, b.Grouping) &&
               ActivityIdsEqual(a.Category, b.Category) &&
               ActivityIdsEqual(a.Other, b.Other);
    }

    private static bool ActivityIdsEqual(XapiList<Activity> a, XapiList<Activity> b) => OrderedEquals(a, b, (x, y) => IriEquals(x.Id, y.Id));

    private static bool ExtensionsEquals(Extensions? a, Extensions? b) =>
        a is null || b is null ? (a?.Count ?? 0) == (b?.Count ?? 0) : a.Equals(b, IgnoreCase);

    private static bool IriEquals(Iri a, Iri b) => string.Equals(a.Value, b.Value, IgnoreCase);

    private static bool StringEquals(string? a, string? b) => string.Equals(a, b, IgnoreCase);

    private static DateTimeOffset? TruncateToMilliseconds(DateTimeOffset? value) =>
        value is { } v ? new DateTimeOffset(v.UtcTicks - (v.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero) : null;

    private static bool OrderedEquals<T>(XapiList<T> a, XapiList<T> b, Func<T, T, bool> equals)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool UnorderedEquals<T>(XapiList<T> a, XapiList<T> b, Func<T, T, bool> equals)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var used = new bool[b.Count];
        foreach (var item in a)
        {
            var match = -1;
            for (var j = 0; j < b.Count; j++)
            {
                if (!used[j] && equals(item, b[j]))
                {
                    match = j;
                    break;
                }
            }

            if (match < 0)
            {
                return false;
            }

            used[match] = true;
        }

        return true;
    }
}
