using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Xunit.Sdk;

namespace Xapi.Core.Tests.Conformance;

public sealed class ConformanceCase : IXunitSerializable
{
    public ConformanceCase()
    {
    }

    internal ConformanceCase(
        string version,
        string id,
        IReadOnlyList<string> path,
        string? requirement,
        string kind,
        string expect,
        string statementJson)
    {
        Version = version;
        Id = id;
        Path = path;
        Requirement = requirement;
        Kind = kind;
        Expect = expect;
        StatementJson = statementJson;
    }

    public string Version { get; private set; } = "";

    public string Id { get; private set; } = "";

    public IReadOnlyList<string> Path { get; private set; } = [];

    public string? Requirement { get; private set; }

    public string Kind { get; private set; } = "";

    public string Expect { get; private set; } = "";

    public bool ExpectValid => Expect == ConformanceCases.ExpectValid;

    public string StatementJson { get; private set; } = "";

    public byte[] GetStatementUtf8() => Encoding.UTF8.GetBytes(StatementJson);

    public override string ToString() => $"{Version} {Id}";

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Version), Version);
        info.AddValue(nameof(Id), Id);
    }

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        var loaded = ConformanceCases.Find(
            info.GetValue<string>(nameof(Version)) ?? "",
            info.GetValue<string>(nameof(Id)) ?? "");
        Version = loaded.Version;
        Id = loaded.Id;
        Path = loaded.Path;
        Requirement = loaded.Requirement;
        Kind = loaded.Kind;
        Expect = loaded.Expect;
        StatementJson = loaded.StatementJson;
    }
}

public sealed record ConformanceBattery(
    string Source,
    string Commit,
    string License,
    string Version,
    IReadOnlyList<ConformanceCase> Cases);

public static class ConformanceCases
{
    public const string Version1_0_3 = "1.0.3";
    public const string Version2_0_0 = "2.0.0";

    public const string KindStatementValidity = "statement-validity";
    public const string KindTimeDependent = "time-dependent";

    public const string ExpectValid = "valid";
    public const string ExpectInvalid = "invalid";

    private const string SolutionFile = "xapi-dotnet.slnx";

    private static readonly ConcurrentDictionary<string, Lazy<ConformanceBattery>> _batteries = new(StringComparer.Ordinal);

    public static IReadOnlyList<string> Versions { get; } = [Version1_0_3, Version2_0_0];

    public static TheoryData<ConformanceCase> V1_0_3 => Rows(Version1_0_3);

    public static TheoryData<ConformanceCase> V2_0_0 => Rows(Version2_0_0);

    public static ConformanceBattery Load(string version) =>
        _batteries.GetOrAdd(version, v => new Lazy<ConformanceBattery>(() => Read(v))).Value;

    public static ConformanceCase Find(string version, string id) =>
        Load(version).Cases.FirstOrDefault(c => c.Id == id)
        ?? throw new KeyNotFoundException($"No conformance case '{id}' for xAPI {version}.");

    public static TheoryData<ConformanceCase> Rows(string version)
    {
        var data = new TheoryData<ConformanceCase>();
        foreach (var testCase in Load(version).Cases)
        {
            data.Add(new TheoryDataRow<ConformanceCase>(testCase) { Label = testCase.ToString() }
                .WithTrait("cts-kind", testCase.Kind)
                .WithTrait("cts-expect", testCase.Expect));
        }

        return data;
    }

    public static string DataFilePath(string version) =>
        Path.Combine(FindRepositoryRoot(), "tests", "Xapi.Core.Tests", "Conformance", $"cts-templates-{version}.json");

    private static ConformanceBattery Read(string version)
    {
        using var stream = File.OpenRead(DataFilePath(version));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var fileVersion = root.GetProperty("version").GetString() ?? "";
        if (fileVersion != version)
        {
            throw new InvalidDataException($"{DataFilePath(version)} declares version '{fileVersion}'.");
        }

        var cases = new List<ConformanceCase>();
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            cases.Add(new ConformanceCase(
                version,
                item.GetProperty("id").GetString() ?? "",
                [.. item.GetProperty("path").EnumerateArray().Select(p => p.GetString() ?? "")],
                item.GetProperty("requirement").GetString(),
                item.GetProperty("kind").GetString() ?? "",
                item.GetProperty("expect").GetString() ?? "",
                item.GetProperty("statement").GetRawText()));
        }

        return new ConformanceBattery(
            root.GetProperty("source").GetString() ?? "",
            root.GetProperty("commit").GetString() ?? "",
            root.GetProperty("license").GetString() ?? "",
            fileVersion,
            cases);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not find {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}
