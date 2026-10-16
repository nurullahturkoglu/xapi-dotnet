// Gate for an ADL LRS conformance test suite run: reads the suite's JSON log and decides pass or fail.
// The suite's exit code cannot be trusted (it wraps at 256 and ignores tests that never ran).
// Usage: dotnet run eng/conformance/CtsGate.cs -- --help

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

const string Usage = """
    usage: dotnet run eng/conformance/CtsGate.cs -- <cts-log.json> [options]

      --floor N           minimum total test count
      --min-passed N      minimum passed count (ratchet)
      --floor-file F      read total, minPassed and strict from F[target][db][xapi]
      --target T          floor file key (lrsql, xapi-dotnet)
      --db D              floor file key (sqlite, postgres)
      --xapi V            expected battery version; the log must report the same
      --no-strict         report failures outside the allowlist without failing on them
      --allowlist F       known failures, one 'Group > Suite > title' path per line, '#' comments
      --junit F           write JUnit XML (allowlisted failures become <skipped>)
      --summary F         append a Markdown summary (e.g. $GITHUB_STEP_SUMMARY)

    --floor and --min-passed override the floor file.
    Exit codes: 0 gate passed, 1 gate failed, 2 bad arguments or unreadable input.
    """;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

Options opt;
try
{
    opt = Options.Parse(args);
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 2;
}

RunLog run;
Limits limits;
HashSet<string> allowlist;
try
{
    run = RunLog.Load(opt.LogPath);
    limits = Limits.Resolve(opt, opt.Xapi ?? run.Version);
    allowlist = opt.AllowlistPath is null ? [] : LoadAllowlist(opt.AllowlistPath);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

var failed = run.Leaves.Where(l => l.Status == "failed").ToList();
var unexpected = failed.Where(l => !allowlist.Contains(l.Path)).ToList();
var allowedCount = failed.Count - unexpected.Count;
var stale = allowlist.Except(failed.Select(l => l.Path)).Order(StringComparer.Ordinal).ToList();
var neverRan = run.Total - run.Passed - run.Failed;

var problems = new List<string>();
if (opt.Xapi is not null && opt.Xapi != run.Version)
{
    problems.Add($"log reports xAPI {run.Version}, expected {opt.Xapi}");
}
if (run.State != "finished")
{
    problems.Add($"run state is '{run.State}', not 'finished'");
}
if (run.Total < limits.Floor)
{
    problems.Add($"total {run.Total} is below the floor {limits.Floor}");
}
if (neverRan > 0)
{
    problems.Add($"{neverRan} tests never ran (root hook failure, bail or crash)");
}
if (run.Passed < limits.MinPassed)
{
    problems.Add($"passed {run.Passed} is below minPassed {limits.MinPassed}");
}
if (limits.Strict && unexpected.Count > 0)
{
    problems.Add($"{unexpected.Count} failures are not in the allowlist");
}

var notes = new List<string>();
notes.AddRange(stale.Select(p => $"allowlisted test passed or no longer exists, remove it: {p}"));
if (limits.Ratchet && run.Passed > limits.MinPassed)
{
    notes.Add($"passed {run.Passed} is above minPassed {limits.MinPassed}; raise it in floor.json");
}
if (!limits.Strict && unexpected.Count > 0)
{
    notes.Add($"{unexpected.Count} failures outside the allowlist (not fatal: strict mode is off)");
}

var ok = problems.Count == 0;
var inGitHub = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

Console.WriteLine(Invariant($"xAPI {run.Version}: state={run.State} total={run.Total} passed={run.Passed} failed={run.Failed} never-ran={neverRan} allowlisted-failures={allowedCount} duration={run.DurationMs}ms"));
Console.WriteLine(Invariant($"limits: floor={limits.Floor} minPassed={limits.MinPassed} strict={limits.Strict}"));
foreach (var leaf in unexpected)
{
    Console.WriteLine($"FAIL {leaf.DisplayPath}");
    Console.WriteLine($"     {FirstLine(leaf.Error)}");
}
foreach (var note in notes)
{
    Console.WriteLine(inGitHub ? $"::warning title=CTS gate::{note}" : $"NOTE {note}");
}
foreach (var problem in problems)
{
    Console.WriteLine(inGitHub ? $"::error title=CTS gate::{problem}" : $"ERROR {problem}");
}
Console.WriteLine(ok ? "gate: PASS" : "gate: FAIL");

if (opt.JUnitPath is not null)
{
    WriteJUnit(opt.JUnitPath, run, allowlist);
}
if (opt.SummaryPath is not null)
{
    File.AppendAllText(opt.SummaryPath, BuildSummary(run, limits, neverRan, allowedCount, unexpected, problems, notes, ok));
}

return ok ? 0 : 1;

static HashSet<string> LoadAllowlist(string path) =>
    File.ReadLines(path)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split(" # ", 2)[0].TrimEnd())
        .ToHashSet(StringComparer.Ordinal);

static string FirstLine(string? text) =>
    string.IsNullOrEmpty(text) ? "" : text.Split('\n', 2)[0].TrimEnd('\r');

static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

static void WriteJUnit(string path, RunLog run, HashSet<string> allowlist)
{
    var cases = run.Leaves.Select(leaf =>
    {
        var testcase = new XElement("testcase",
            new XAttribute("classname", leaf.Suite.Length > 0 ? leaf.Suite : "(root)"),
            new XAttribute("name", leaf.Title.Length > 0 ? leaf.Title : "(root hook)"));
        if (leaf.Status == "failed")
        {
            var tag = allowlist.Contains(leaf.Path) ? "skipped" : "failure";
            var message = allowlist.Contains(leaf.Path) ? $"allowlisted: {FirstLine(leaf.Error)}" : FirstLine(leaf.Error);
            testcase.Add(new XElement(tag, new XAttribute("message", Clean(message)), Clean(leaf.Error ?? "")));
        }
        else if (leaf.Status != "passed")
        {
            testcase.Add(new XElement("skipped", new XAttribute("message", $"status '{leaf.Status}': did not run")));
        }
        return testcase;
    }).ToList();

    var failures = cases.Count(c => c.Element("failure") is not null);
    var skipped = cases.Count(c => c.Element("skipped") is not null);
    var suite = new XElement("testsuite",
        new XAttribute("name", $"adl-cts-{run.Version}"),
        new XAttribute("tests", cases.Count),
        new XAttribute("failures", failures),
        new XAttribute("errors", 0),
        new XAttribute("skipped", skipped),
        new XAttribute("time", (run.DurationMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)),
        cases);
    using var writer = XmlWriter.Create(path, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
    new XDocument(new XElement("testsuites", suite)).Save(writer);
}

static string Clean(string text)
{
    var sb = new StringBuilder(text.Length);
    foreach (var c in text)
    {
        if (XmlConvert.IsXmlChar(c) || char.IsSurrogate(c))
        {
            sb.Append(c);
        }
    }
    return sb.ToString();
}

static string BuildSummary(RunLog run, Limits limits, int neverRan, int allowedCount, List<Leaf> unexpected, List<string> problems, List<string> notes, bool ok)
{
    const int MaxListed = 50;
    var sb = new StringBuilder();
    var inv = CultureInfo.InvariantCulture;
    sb.AppendLine(inv, $"### ADL CTS xAPI {run.Version}: {(ok ? "passed" : "FAILED")}");
    sb.AppendLine();
    sb.AppendLine("| State | Total | Passed | Failed | Never ran | Allowlisted | Floor | Min passed | Duration |");
    sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
    sb.AppendLine(inv, $"| {run.State} | {run.Total} | {run.Passed} | {run.Failed} | {neverRan} | {allowedCount} | {limits.Floor} | {limits.MinPassed} | {run.DurationMs / 1000.0:0.0} s |");
    sb.AppendLine();
    foreach (var problem in problems)
    {
        sb.AppendLine(inv, $"- :x: {problem}");
    }
    foreach (var note in notes)
    {
        sb.AppendLine(inv, $"- :warning: {note}");
    }
    if (unexpected.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine(inv, $"<details><summary>Failures outside the allowlist ({unexpected.Count})</summary>");
        sb.AppendLine();
        foreach (var leaf in unexpected.Take(MaxListed))
        {
            sb.AppendLine(inv, $"- `{Escape(leaf.DisplayPath)}`<br>{Escape(FirstLine(leaf.Error))}");
        }
        if (unexpected.Count > MaxListed)
        {
            sb.AppendLine(inv, $"- ... and {unexpected.Count - MaxListed} more (see the JUnit report)");
        }
        sb.AppendLine();
        sb.AppendLine("</details>");
    }
    sb.AppendLine();
    return sb.ToString();

    static string Escape(string s) => s.Replace("`", "'", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
}

internal sealed record Leaf(string Path, string Suite, string Title, string Status, string? Error)
{
    public string DisplayPath => Path.Length > 0 ? Path : "(root hook)";
}

internal sealed record RunLog(string State, string Version, int Total, int Passed, int Failed, long DurationMs, List<Leaf> Leaves)
{
    public static RunLog Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var summary = root.GetProperty("summary");
        var leaves = new List<Leaf>();
        if (root.TryGetProperty("log", out var tree) && tree.ValueKind == JsonValueKind.Object)
        {
            Walk(tree, [], leaves);
        }
        return new RunLog(
            Text(root, "state") ?? "(missing)",
            Text(summary, "version") ?? "(missing)",
            Number(summary, "total"),
            Number(summary, "passed"),
            Number(summary, "failed"),
            root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0,
            leaves);
    }

    private static void Walk(JsonElement node, List<string> path, List<Leaf> leaves)
    {
        var title = (Text(node, "title") ?? "").Trim();
        var here = title.Length > 0 ? [.. path, title] : path;
        if (!node.TryGetProperty("tests", out var tests) || tests.ValueKind != JsonValueKind.Array || tests.GetArrayLength() == 0)
        {
            leaves.Add(new Leaf(
                string.Join(" > ", here),
                string.Join(" > ", here.Take(Math.Max(0, here.Count - 1))),
                title,
                Text(node, "status") ?? "",
                Text(node, "error")));
            return;
        }
        foreach (var child in tests.EnumerateArray())
        {
            Walk(child, here, leaves);
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}

internal sealed record Limits(int Floor, int MinPassed, bool Strict, bool Ratchet)
{
    public static Limits Resolve(Options opt, string xapi)
    {
        int? floor = null, minPassed = null;
        bool? strict = null;
        if (opt.FloorFile is not null)
        {
            if (opt.Target is null || opt.Db is null)
            {
                throw new FormatException("--floor-file needs --target and --db");
            }
            var entry = JsonNode.Parse(File.ReadAllText(opt.FloorFile))?[opt.Target]?[opt.Db]?[xapi]
                ?? throw new KeyNotFoundException($"{opt.FloorFile} has no entry {opt.Target}.{opt.Db}.{xapi}");
            floor = entry["total"]?.GetValue<int>();
            minPassed = entry["minPassed"]?.GetValue<int>();
            strict = entry["strict"]?.GetValue<bool>();
        }
        return new Limits(
            opt.Floor ?? floor ?? throw new FormatException("no floor: pass --floor or --floor-file"),
            opt.MinPassed ?? minPassed ?? 0,
            !opt.NoStrict && (strict ?? true),
            (opt.MinPassed ?? minPassed) is not null);
    }
}

internal sealed record Options(
    string LogPath, int? Floor, int? MinPassed, string? FloorFile, string? Target, string? Db, string? Xapi,
    bool NoStrict, string? AllowlistPath, string? JUnitPath, string? SummaryPath)
{
    public static Options Parse(string[] args)
    {
        string? log = null, floorFile = null, target = null, db = null, xapi = null, allowlist = null, junit = null, summary = null;
        int? floor = null, minPassed = null;
        var noStrict = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Value() => ++i < args.Length ? args[i] : throw new FormatException($"{arg} needs a value");
            int Count() => int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException($"{arg} needs a non-negative integer");
            switch (arg)
            {
                case "--floor": floor = Count(); break;
                case "--min-passed": minPassed = Count(); break;
                case "--floor-file": floorFile = Value(); break;
                case "--target": target = Value(); break;
                case "--db": db = Value(); break;
                case "--xapi": xapi = Value(); break;
                case "--no-strict": noStrict = true; break;
                case "--allowlist": allowlist = Value(); break;
                case "--junit": junit = Value(); break;
                case "--summary": summary = Value(); break;
                default:
                    if (arg.StartsWith('-') || log is not null)
                    {
                        throw new FormatException($"unexpected argument '{arg}'");
                    }
                    log = arg;
                    break;
            }
        }
        return new Options(log ?? throw new FormatException("missing log path"), floor, minPassed, floorFile, target, db, xapi, noStrict, allowlist, junit, summary);
    }
}
