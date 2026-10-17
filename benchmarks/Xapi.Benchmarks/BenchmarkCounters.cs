using System.Globalization;
using System.Text;

namespace Xapi.Benchmarks;

// Counts that BenchmarkDotNet has no column for (LRS write retries). Each benchmark runs in a process of its own,
// so the counts are appended to files in a directory that Program creates and passes down, and Program prints
// them after the run.
public static class BenchmarkCounters
{
    public const string DirectoryVariable = "XAPI_BENCH_COUNTERS";

    public static void Record(string name, long statements, long retries)
    {
        if (Environment.GetEnvironmentVariable(DirectoryVariable) is not { Length: > 0 } directory)
        {
            return;
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{name}\t{statements}\t{retries}\n");
        File.AppendAllText(Path.Combine(directory, $"{Environment.ProcessId}.tsv"), line);
    }

    public static string? Summarize(string directory)
    {
        var rows = Directory.EnumerateFiles(directory, "*.tsv")
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 3)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (
                Name: group.Key,
                Statements: group.Sum(parts => long.Parse(parts[1], CultureInfo.InvariantCulture)),
                Retries: group.Sum(parts => long.Parse(parts[2], CultureInfo.InvariantCulture))))
            .ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var table = new StringBuilder()
            .AppendLine("| Concurrent writers | Statements posted | Store retries | Retries per 1,000 statements |")
            .AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var (name, statements, retries) in rows)
        {
            var rate = statements == 0 ? 0 : retries * 1000.0 / statements;
            table.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {statements:N0} | {retries:N0} | {rate:F1} |");
        }

        return table.ToString();
    }
}
