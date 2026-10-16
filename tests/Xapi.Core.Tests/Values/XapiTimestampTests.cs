namespace Xapi.Core.Tests.Values;

public class XapiTimestampTests
{
    [Theory]
    [InlineData("2008-09-15T15:53:00.601+00:00", "2008-09-15T15:53:00.6010000Z")]
    [InlineData("2008-09-15T15:53:00Z", "2008-09-15T15:53:00.0000000Z")]
    [InlineData("2008-09-15t15:53:00z", "2008-09-15T15:53:00.0000000Z")]
    [InlineData("2008-09-15T18:53:00.5+03:00", "2008-09-15T15:53:00.5000000Z")]
    [InlineData("2008-09-15T10:23:00-05:30", "2008-09-15T15:53:00.0000000Z")]
    [InlineData("2008-09-15T15:53:00.123456789Z", "2008-09-15T15:53:00.1234567Z")]
    [InlineData("2016-12-31T23:59:60Z", "2017-01-01T00:00:00.0000000Z")]
    [InlineData("2024-02-29T00:00:00Z", "2024-02-29T00:00:00.0000000Z")]
    public void Parses_rfc3339_and_converts_to_utc(string text, string expectedUtc)
    {
        foreach (var version in new[] { XapiVersion.V1_0_3, XapiVersion.V2_0_0 })
        {
            Assert.True(XapiTimestamp.TryParse(text, version, out var timestamp));
            Assert.Equal(TimeSpan.Zero, timestamp.Offset);
            Assert.Equal(expectedUtc, timestamp.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("should fail")]
    [InlineData("01/011/2015")]
    [InlineData("2008-09-15T15:53:00.601-00")]
    [InlineData("2008-09-15T15:53:00.601-0000")]
    [InlineData("2008-09-15T15:53:00.601-00:00")]
    [InlineData("2008-09-15T15:53:00")]
    [InlineData("2008-09-15")]
    [InlineData("2008-09-15T15:53:00.Z")]
    [InlineData("2008-13-15T15:53:00Z")]
    [InlineData("2023-02-29T00:00:00Z")]
    [InlineData("2008-09-15T24:00:00Z")]
    [InlineData("2008-09-15T15:53:00+24:00")]
    [InlineData("2008-09-15T15:53:00Z ")]
    public void Rejects_invalid_timestamps(string text)
    {
        Assert.False(XapiTimestamp.TryParse(text, XapiVersion.V2_0_0, out _));
        Assert.Throws<FormatException>(() => XapiTimestamp.Parse(text, XapiVersion.V2_0_0));
    }

    [Fact]
    public void Space_separator_is_allowed_only_in_2_0()
    {
        const string Text = "2008-09-15 15:53:00Z";
        Assert.True(XapiTimestamp.TryParse(Text, XapiVersion.V2_0_0, out _));
        Assert.False(XapiTimestamp.TryParse(Text, XapiVersion.V1_0_3, out _));
    }

    [Theory]
    [InlineData(0, "2026-10-16T12:00:00.000Z")]
    [InlineData(5_900_000, "2026-10-16T12:00:00.590Z")]
    [InlineData(6_000_000, "2026-10-16T12:00:00.600Z")]
    [InlineData(1_234_567, "2026-10-16T12:00:00.1234567Z")]
    public void Format_writes_utc_with_fixed_fraction_digits(long extraTicks, string expected)
    {
        var value = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.FromHours(3)).AddTicks(extraTicks);
        Assert.Equal(expected, XapiTimestamp.Format(value));
    }
}
