using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Testcontainers.PostgreSql;
using Xapi.Benchmarks;
using Xapi.Benchmarks.Lrs;

// dotnet run -c Release --project benchmarks/Xapi.Benchmarks -- --filter '*'
// Without a --job argument every benchmark runs a short job (see docs/benchmarks.md); pass --job Default for
// BenchmarkDotNet's full statistics. XAPI_BENCH_POSTGRES=1 adds PostgreSQL to the LRS benchmarks.

// Same image as the conformance job and the LRS tests (eng/conformance/cts.env)
const string PostgreSqlImage = "postgres:17.11-alpine@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24";

IConfig config = DefaultConfig.Instance;
if (!args.Any(a => a is "--job" or "-j"))
{
    config = config.AddJob(Job.Default
        .WithLaunchCount(1)
        .WithWarmupCount(3)
        .WithIterationCount(5)
        .WithId("Short"));
}

var counters = Path.Combine(config.ArtifactsPath, "counters", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
Directory.CreateDirectory(counters);
Environment.SetEnvironmentVariable(BenchmarkCounters.DirectoryVariable, counters);

PostgreSqlContainer? postgres = null;
if (Environment.GetEnvironmentVariable(LrsDatabases.PostgreSqlSwitch) == "1" &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LrsDatabases.PostgreSqlConnectionVariable)))
{
    // One server for the whole run; each benchmark process creates a database of its own on it
    postgres = new PostgreSqlBuilder(PostgreSqlImage).Build();
    await postgres.StartAsync();
    Environment.SetEnvironmentVariable(LrsDatabases.PostgreSqlConnectionVariable, postgres.GetConnectionString());
}

try
{
    BenchmarkSwitcher.FromAssembly(typeof(BenchmarkCounters).Assembly).Run(args, config);
}
finally
{
    if (postgres is not null)
    {
        await postgres.DisposeAsync();
    }
}

if (BenchmarkCounters.Summarize(counters) is { } summary)
{
    Console.WriteLine();
    Console.WriteLine("// LRS statement store retries");
    Console.WriteLine(summary);
    await File.WriteAllTextAsync(Path.Combine(config.ArtifactsPath, "results", "lrs-retries.md"), summary);
}
