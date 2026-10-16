namespace Xapi.Core.Tests.Values;

public class XapiVersionTests
{
    [Theory]
    [InlineData("1.0", "1.0.3")]
    [InlineData("1.0.0", "1.0.3")]
    [InlineData("1.0.3", "1.0.3")]
    [InlineData("1.0.9", "1.0.3")]
    [InlineData("2.0", "2.0.0")]
    [InlineData("2.0.0", "2.0.0")]
    [InlineData("2.0.1", "2.0.0")]
    [InlineData(" 2.0.0 ", "2.0.0")]
    public void TryParseHeader_maps_supported_versions_to_a_profile(string header, string expected)
    {
        Assert.True(XapiVersion.TryParseHeader(header, out var version));
        Assert.Equal(expected, version.HeaderValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.95")]
    [InlineData("1.1.0")]
    [InlineData("2.1.0")]
    [InlineData("3.0.0")]
    [InlineData("2.0.")]
    [InlineData("2.0.0-beta")]
    [InlineData("2")]
    public void TryParseHeader_rejects_other_versions(string? header) =>
        Assert.False(XapiVersion.TryParseHeader(header, out _));

    [Fact]
    public void Versions_compare_by_profile()
    {
        Assert.Equal(XapiVersion.V2_0_0, default(XapiVersion));
        Assert.NotEqual(XapiVersion.V1_0_3, XapiVersion.V2_0_0);
        Assert.True(XapiVersion.V1_0_3 == XapiVersion.V1_0_3);
        Assert.Equal("1.0.3", XapiVersion.V1_0_3.ToString());
        Assert.False(XapiVersion.V1_0_3.IsV2);
    }
}
