using System.Diagnostics;

namespace Xapi.Client.Outbox.Tests.Fakes;

internal static class Samples
{
    public static Agent Learner { get; } = Agent.FromMbox("learner@example.com", "Learner");

    /// <summary>A Statement about activity <paramref name="n"/>, so tests can tell Statements apart by content.</summary>
    public static Statement Statement(int n, Guid? id = null) =>
        new(Learner, Verbs.Completed, new Activity(ActivityId(n))) { Id = id };

    public static Iri ActivityId(int n) => new($"https://example.com/activities/{n}");

    public static int Number(Statement statement) =>
        int.Parse(((Activity)statement.Object).Id.ToString().Split('/').Last(), System.Globalization.CultureInfo.InvariantCulture);

    public static Guid Id(int n) => new($"00000000-0000-4000-8000-{n:D12}");

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Polls until the condition holds; for work done by the background loop on another thread.</summary>
    public static async Task Eventually(Func<bool> condition, string because)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(10, Ct);
        }
    }

    public static async Task Eventually(Func<Task<bool>> condition, string because)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(10, Ct);
        }
    }
}

/// <summary>A <see cref="Random"/> whose <see cref="Random.NextDouble"/> always returns the same value.</summary>
internal sealed class FixedRandom(double value) : Random
{
    protected override double Sample() => value;
}
