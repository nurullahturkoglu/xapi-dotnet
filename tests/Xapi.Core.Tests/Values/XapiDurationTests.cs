namespace Xapi.Core.Tests.Values;

public class XapiDurationTests
{
    [Theory]
    [InlineData("PT1H0M0S")]
    [InlineData("PT4H35M59.14S")]
    [InlineData("PT16559.14S")]
    [InlineData("P3Y1M29DT4H35M59.14S")]
    [InlineData("P3Y")]
    [InlineData("P4W")]
    [InlineData("PT0S")]
    [InlineData("P1D")]
    [InlineData("PT1.5H")]
    [InlineData("PT0.001S")]
    public void Accepts_iso8601_durations(string text)
    {
        Assert.True(XapiDuration.TryParse(text, out var duration));
        Assert.Equal(text, duration.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("PT")]
    [InlineData("P1DT")]
    [InlineData("PA1H0M0S")]
    [InlineData("P4W1D")]
    [InlineData("P1D4W")]
    [InlineData("pt1h")]
    [InlineData("PT1M1H")]
    [InlineData("PT1.5H30M")]
    [InlineData("PT1,5S")]
    [InlineData("PT.5S")]
    [InlineData("PT1.S")]
    [InlineData("P1H")]
    [InlineData("PT1D")]
    [InlineData("P0003-01-29T04:35:59")]
    [InlineData("-PT1S")]
    [InlineData("should fail")]
    public void Rejects_other_formats(string text)
    {
        Assert.False(XapiDuration.TryParse(text, out _));
        Assert.Throws<FormatException>(() => XapiDuration.Parse(text));
    }

    [Theory]
    [InlineData("PT1.234S", "PT1.23S")]
    [InlineData("PT1.239S", "PT1.23S")]
    [InlineData("PT1S", "PT1.00S")]
    [InlineData("P1D", "P1DT0S")]
    public void Equality_ignores_precision_beyond_hundredths(string left, string right)
    {
        Assert.Equal(XapiDuration.Parse(left), XapiDuration.Parse(right));
        Assert.Equal(XapiDuration.Parse(left).GetHashCode(), XapiDuration.Parse(right).GetHashCode());
    }

    [Theory]
    [InlineData("PT1.23S", "PT1.24S")]
    [InlineData("PT60S", "PT1M")]
    [InlineData("P1W", "P7D")]
    public void Different_durations_are_not_equal(string left, string right) =>
        Assert.NotEqual(XapiDuration.Parse(left), XapiDuration.Parse(right));

    [Fact]
    public void ToTimeSpan_uses_nominal_lengths()
    {
        Assert.Equal(new TimeSpan(4, 35, 59) + TimeSpan.FromMilliseconds(140), XapiDuration.Parse("PT4H35M59.14S").ToTimeSpan());
        Assert.Equal(TimeSpan.FromDays(365 + 30 + 1), XapiDuration.Parse("P1Y1M1D").ToTimeSpan());
        Assert.Equal(TimeSpan.FromDays(28), XapiDuration.Parse("P4W").ToTimeSpan());
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, "PT0S")]
    [InlineData(0, 1, 2, 3, 450, "PT1H2M3.45S")]
    [InlineData(2, 0, 0, 0, 0, "P2D")]
    [InlineData(0, 0, 5, 0, 0, "PT5M")]
    [InlineData(0, 0, 0, 0, 9, "PT0S")]
    [InlineData(1, 0, 0, 0, 500, "P1DT0.5S")]
    public void FromTimeSpan_writes_seconds_to_hundredths(int days, int hours, int minutes, int seconds, int milliseconds, string expected) =>
        Assert.Equal(expected, XapiDuration.FromTimeSpan(new TimeSpan(days, hours, minutes, seconds, milliseconds)).ToString());

    [Fact]
    public void FromTimeSpan_rejects_negative_values() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => XapiDuration.FromTimeSpan(TimeSpan.FromSeconds(-1)));

    [Fact]
    public void Default_is_zero() => Assert.Equal("PT0S", default(XapiDuration).ToString());
}
