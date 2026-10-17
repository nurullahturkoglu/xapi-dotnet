using Xapi.Json;
using Xapi.Lrs.Statements;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// Turns stored statements into the UTF-8 JSON of the requested format (IEEE 9274.1.1, 4.1.6.1 GET "format").
internal static class StatementRendering
{
    public static async Task<List<ReadOnlyMemory<byte>>> RenderAsync(
        IReadOnlyList<StoredStatement> statements,
        StatementFormat format,
        IReadOnlyList<string> acceptLanguages,
        IStatementStore store,
        CancellationToken cancellationToken)
    {
        if (format == StatementFormat.Exact)
        {
            return [.. statements.Select(s => s.Utf8Json)];
        }

        var parsed = statements.Select(s => (Statement: Read(s), Version: VersionOf(s))).ToList();
        IReadOnlyDictionary<Iri, Activity>? canonical = null;
        if (format == StatementFormat.Canonical)
        {
            var ids = parsed.SelectMany(p => StatementIndex.Build(p.Statement).Activities).Select(a => a.Id).ToHashSet();
            canonical = await store.GetActivitiesAsync(ids, cancellationToken).ConfigureAwait(false);
        }

        return parsed.ConvertAll(p => StatementJson.Write(
            canonical is null ? p.Statement : WithCanonicalActivities(p.Statement, canonical),
            p.Version,
            format,
            acceptLanguages));
    }

    // "canonical": Activity Objects populated with the canonical definition as determined by the LRS. The LRS's
    // definition is the merge of the definitions it accepted (EfStatementStore), so it replaces the one in the
    // statement; an Activity the LRS holds no definition for keeps its own. Language filtering is applied when
    // writing.
    private static Statement WithCanonicalActivities(Statement statement, IReadOnlyDictionary<Iri, Activity> canonical)
    {
        Activity Replace(Activity activity) =>
            canonical.TryGetValue(activity.Id, out var known) && known.Definition is { } definition
                ? activity with { Definition = definition }
                : activity;

        XapiList<Activity> ReplaceAll(XapiList<Activity> activities) =>
            activities.IsEmpty ? activities : XapiList.CreateRange(activities.Select(Replace));

        Context? ReplaceInContext(Context? context) =>
            context?.ContextActivities is { } activities
                ? context with
                {
                    ContextActivities = activities with
                    {
                        Parent = ReplaceAll(activities.Parent),
                        Grouping = ReplaceAll(activities.Grouping),
                        Category = ReplaceAll(activities.Category),
                        Other = ReplaceAll(activities.Other),
                    },
                }
                : context;

        StatementObject ReplaceInObject(StatementObject value) => value switch
        {
            Activity activity => Replace(activity),
            SubStatement sub => sub with { Object = ReplaceInObject(sub.Object), Context = ReplaceInContext(sub.Context) },
            _ => value,
        };

        return statement with { Object = ReplaceInObject(statement.Object), Context = ReplaceInContext(statement.Context) };
    }

    private static Statement Read(StoredStatement stored)
    {
        // Stored payloads were validated on the way in; 2.0.0 reading accepts everything 1.0.3 does
        var parsed = XapiJson.ParseStatement(stored.Utf8Json.Span, XapiVersion.V2_0_0, XapiReadMode.Lenient);
        return parsed.IsSuccess
            ? parsed.Value
            : throw new InvalidOperationException("A stored statement could not be read: " + string.Join("; ", parsed.Errors));
    }

    private static XapiVersion VersionOf(StoredStatement stored) =>
        stored.Version.StartsWith("2.", StringComparison.Ordinal) ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3;
}
