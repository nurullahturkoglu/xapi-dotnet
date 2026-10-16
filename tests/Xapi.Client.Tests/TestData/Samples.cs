using Xapi.Json;

namespace Xapi.Client.Tests.TestData;

internal static class Samples
{
    public static Agent Learner { get; } = Agent.FromMbox("learner@example.com", "Learner");

    public static Iri ActivityId { get; } = new("https://example.com/activities/course-1");

    public static Statement Statement(Guid? id = null) =>
        new(Learner, Verbs.Completed, new Activity(ActivityId)) { Id = id };

    public static Guid Id(int n) => new($"00000000-0000-4000-8000-{n:D12}");

    /// <summary>A Statement as an LRS returns it: with id, stored, authority and version.</summary>
    public static string StoredJson(Guid id, XapiVersion version) =>
        XapiJson.SerializeStatement(
            Statement(id) with
            {
                Stored = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero),
                Timestamp = new DateTimeOffset(2026, 10, 17, 7, 59, 0, TimeSpan.Zero),
                Authority = Agent.FromMbox("authority@example.com"),
                Version = version.HeaderValue,
            },
            version);

    public static string StatementResultJson(XapiVersion version, string? more, params Guid[] ids) =>
        $$"""{"statements":[{{string.Join(",", ids.Select(id => StoredJson(id, version)))}}]{{(more is null ? "" : $",\"more\":\"{more}\"")}}}""";
}
