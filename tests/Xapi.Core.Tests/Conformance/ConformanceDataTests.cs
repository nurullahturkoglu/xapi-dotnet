using System.Text.Json;

namespace Xapi.Core.Tests.Conformance;

public class ConformanceDataTests
{
    private const string Commit = "5bc232d349c60faded8240da698f195106091638";

    [Theory]
    [InlineData(ConformanceCases.Version1_0_3, 950)]
    [InlineData(ConformanceCases.Version2_0_0, 972)]
    public void Battery_has_the_suite_template_case_count(string version, int expected) =>
        Assert.Equal(expected, ConformanceCases.Load(version).Cases.Count);

    [Theory]
    [InlineData(ConformanceCases.Version1_0_3)]
    [InlineData(ConformanceCases.Version2_0_0)]
    public void Battery_header_names_source_commit_and_license(string version)
    {
        var battery = ConformanceCases.Load(version);

        Assert.Equal("adlnet/lrs-conformance-test-suite", battery.Source);
        Assert.Equal(Commit, battery.Commit);
        Assert.Equal("MIT", battery.License);
        Assert.Equal(version, battery.Version);
    }

    [Theory]
    [InlineData(ConformanceCases.Version1_0_3)]
    [InlineData(ConformanceCases.Version2_0_0)]
    public void Case_ids_are_unique(string version)
    {
        var duplicates = ConformanceCases.Load(version).Cases
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData(ConformanceCases.Version1_0_3)]
    [InlineData(ConformanceCases.Version2_0_0)]
    public void Every_case_has_a_known_expect_and_kind(string version)
    {
        string[] expects = [ConformanceCases.ExpectValid, ConformanceCases.ExpectInvalid];
        string[] kinds = [ConformanceCases.KindStatementValidity, ConformanceCases.KindTimeDependent];

        Assert.All(ConformanceCases.Load(version).Cases, c =>
        {
            Assert.Contains(c.Expect, expects);
            Assert.Contains(c.Kind, kinds);
            Assert.NotEmpty(c.Path);
            Assert.All(c.Path, segment => Assert.False(string.IsNullOrWhiteSpace(segment)));
            if (c.Requirement is not null)
            {
                Assert.Matches("^XAPI-[0-9]{5}$", c.Requirement);
            }
        });
    }

    [Theory]
    [InlineData(ConformanceCases.Version1_0_3)]
    [InlineData(ConformanceCases.Version2_0_0)]
    public void Find_returns_the_case_with_that_id(string version)
    {
        var first = ConformanceCases.Load(version).Cases[0];

        Assert.Same(first, ConformanceCases.Find(version, first.Id));
    }

    [Theory]
    [MemberData(nameof(ConformanceCases.V1_0_3), MemberType = typeof(ConformanceCases))]
    [MemberData(nameof(ConformanceCases.V2_0_0), MemberType = typeof(ConformanceCases))]
    public void Statement_json_is_a_non_empty_object_or_array(ConformanceCase testCase)
    {
        Assert.False(string.IsNullOrWhiteSpace(testCase.StatementJson));
        Assert.NotEmpty(testCase.GetStatementUtf8());

        using var document = JsonDocument.Parse(testCase.StatementJson);

        Assert.True(
            document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            $"{testCase} has a {document.RootElement.ValueKind} statement.");
    }
}
