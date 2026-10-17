using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Benchmarks.Core;

// Reading, writing, validating and comparing one statement or a batch of 100, from and to UTF-8. Each category has
// a plain System.Text.Json baseline over the same bytes, so the Ratio column is the cost of the xAPI model and its
// validation on top of tokenizing the JSON.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams)]
[CategoriesColumn]
public class StatementJsonBenchmarks
{
    private static readonly XapiVersion Version = XapiVersion.V2_0_0;

    private readonly ArrayBufferWriter<byte> _buffer = new(256 * 1024);
    private Utf8JsonWriter _writer = null!;
    private byte[] _utf8 = [];
    private JsonDocument _document = null!;
    private Statement[] _statements = [];
    private Statement[] _copies = [];

    [Params(1, 100)]
    public int Statements { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var json = Statements == 1
            ? StatementSamples.Statement(Guid.NewGuid(), 7)
            : StatementSamples.Batch(Statements);
        _utf8 = StatementSamples.Utf8(json);
        _document = JsonDocument.Parse(_utf8);
        _statements = Statements == 1
            ? [XapiJson.ParseStatement(_utf8, Version).GetValueOrThrow()]
            : [.. XapiJson.ParseStatements(_utf8, Version).GetValueOrThrow()];

        // Equivalence is checked between two separately parsed instances, as the LRS does on a repeated write
        _copies = Statements == 1
            ? [XapiJson.ParseStatement(_utf8, Version).GetValueOrThrow()]
            : [.. XapiJson.ParseStatements(_utf8, Version).GetValueOrThrow()];
        _writer = new Utf8JsonWriter(_buffer);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _writer.Dispose();
        _document.Dispose();
    }

    [Benchmark(Baseline = true, Description = "JsonDocument.Parse")]
    [BenchmarkCategory("Parse")]
    public int ParseJsonDocument()
    {
        using var document = JsonDocument.Parse(_utf8);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 1;
    }

    [Benchmark(Description = "XapiJson.Parse strict")]
    [BenchmarkCategory("Parse")]
    public int ParseStrict() => Parse(XapiReadMode.Strict);

    [Benchmark(Description = "XapiJson.Parse lenient")]
    [BenchmarkCategory("Parse")]
    public int ParseLenient() => Parse(XapiReadMode.Lenient);

    [Benchmark(Baseline = true, Description = "JsonElement.WriteTo")]
    [BenchmarkCategory("Serialize")]
    public int WriteJsonElement()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset();
        _document.RootElement.WriteTo(_writer);
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    [Benchmark(Description = "XapiJson.Write exact")]
    [BenchmarkCategory("Serialize")]
    public int WriteStatements()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset();
        if (Statements == 1)
        {
            XapiJson.WriteStatement(_writer, _statements[0], Version);
        }
        else
        {
            XapiJson.WriteStatements(_writer, _statements, Version);
        }

        _writer.Flush();
        return _buffer.WrittenCount;
    }

    [Benchmark(Description = "StatementValidator")]
    [BenchmarkCategory("Validate")]
    public int Validate() => Statements == 1
        ? StatementValidator.Validate(_statements[0], Version).Count
        : StatementValidator.ValidateBatch(_statements, Version).Count;

    [Benchmark(Description = "StatementEquivalence")]
    [BenchmarkCategory("Equivalence")]
    public int Equivalence()
    {
        var equivalent = 0;
        for (var i = 0; i < _statements.Length; i++)
        {
            if (StatementEquivalence.AreEquivalent(_statements[i], _copies[i]))
            {
                equivalent++;
            }
        }

        return equivalent;
    }

    private int Parse(XapiReadMode mode)
    {
        if (Statements == 1)
        {
            return XapiJson.ParseStatement(_utf8, Version, mode).IsSuccess ? 1 : 0;
        }

        var result = XapiJson.ParseStatements(_utf8, Version, mode);
        return result.IsSuccess ? result.Value.Count : 0;
    }
}
