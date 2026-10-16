using Xapi.Json;

namespace Xapi.Core.Tests.Conformance;

public class CtsTemplateCaseTests
{
    [Theory]
    [MemberData(nameof(ConformanceCases.V1_0_3), MemberType = typeof(ConformanceCases))]
    public void Parser_agrees_with_the_1_0_3_battery(ConformanceCase testCase) =>
        AssertParserAgrees(testCase, XapiVersion.V1_0_3);

    [Theory]
    [MemberData(nameof(ConformanceCases.V2_0_0), MemberType = typeof(ConformanceCases))]
    public void Parser_agrees_with_the_2_0_0_battery(ConformanceCase testCase) =>
        AssertParserAgrees(testCase, XapiVersion.V2_0_0);

    private static void AssertParserAgrees(ConformanceCase testCase, XapiVersion version)
    {
        var result = XapiJson.ParseStatements(testCase.GetStatementUtf8(), version);

        if (testCase.ExpectValid)
        {
            Assert.True(result.IsSuccess, $"{testCase} should be accepted: {string.Join("; ", result.Errors)}");
        }
        else
        {
            Assert.False(result.IsSuccess, $"{testCase} should be rejected.");
        }
    }
}
