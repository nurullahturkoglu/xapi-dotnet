namespace Xapi.Client.IntegrationTests;

/// <summary>Fresh identifiers per test, so tests can share one LRS and run in parallel.</summary>
internal static class Data
{
    public static TheoryData<string> Versions => ["1.0.3", "2.0.0"];

    public static XapiVersion Parse(string version) =>
        XapiVersion.TryParseHeader(version, out var parsed) ? parsed : throw new ArgumentException(version, nameof(version));

    public static Agent NewAgent() => Agent.FromMbox($"learner-{Guid.NewGuid():N}@example.com", "Learner");

    public static Iri NewActivityId() => new($"https://example.com/activities/{Guid.NewGuid():N}");

    public static Statement NewStatement(Agent? actor = null, Iri? activityId = null, Guid? registration = null) =>
        new(actor ?? NewAgent(), Verbs.Completed, new Activity(activityId ?? NewActivityId())
        {
            Definition = new ActivityDefinition { Name = LanguageMap.Create("en-US", "Integration course") },
        })
        {
            Context = registration is null ? null : new Context { Registration = registration },
            Result = new Result { Success = true, Score = new Score { Scaled = 0.75 } },
            Timestamp = new DateTimeOffset(2026, 10, 17, 9, 30, 15, 123, TimeSpan.Zero),
        };
}
