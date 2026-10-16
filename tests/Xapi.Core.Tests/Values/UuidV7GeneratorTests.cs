namespace Xapi.Core.Tests.Values;

public class UuidV7GeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ids_are_version_7_variant_2_with_the_clock_time()
    {
        var id = new UuidV7Generator(() => Now).NewGuid();
        var text = id.ToString();
        Assert.Equal('7', text[14]);
        Assert.Contains(text[19], "89ab");
        Assert.Equal(Now, UuidV7Generator.GetTimestamp(id));
    }

    [Fact]
    public void Ids_increase_within_the_same_millisecond()
    {
        var generator = new UuidV7Generator(() => Now);
        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.NewGuid().ToString()).ToList();
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Ids_keep_increasing_when_the_clock_goes_backwards()
    {
        var time = Now;
        var generator = new UuidV7Generator(() => time);
        var first = generator.NewGuid().ToString();
        time = Now.AddSeconds(-10);
        var second = generator.NewGuid().ToString();
        Assert.True(string.CompareOrdinal(first, second) < 0);
        Assert.Equal(Now, UuidV7Generator.GetTimestamp(Guid.Parse(second)));
    }

    [Fact]
    public void Ids_increase_across_threads()
    {
        var generator = UuidV7Generator.Shared;
        var ids = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        Parallel.For(0, 4, _ =>
        {
            for (var i = 0; i < 1000; i++)
            {
                ids.Add(generator.NewGuid());
            }
        });
        Assert.Equal(4000, ids.Distinct().Count());
    }

    [Fact]
    public void GetTimestamp_rejects_other_versions() =>
        Assert.Throws<ArgumentException>(() => UuidV7Generator.GetTimestamp(Guid.NewGuid()));
}
