using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Xapi.Internal;

namespace Xapi;

/// <summary>Reads and writes xAPI timestamps (<c>timestamp</c>, <c>stored</c>).</summary>
/// <remarks>
/// <para>
/// Reading accepts RFC 3339 date-times: <c>YYYY-MM-DDTHH:MM:SS[.fraction](Z|±HH:MM)</c>, with <c>t</c> and
/// <c>z</c> also allowed in lower case. xAPI 2.0 also allows a space instead of <c>T</c>. A UTC offset is
/// required and <c>-00:00</c> ("unknown local offset", RFC 3339 4.3) is rejected, as the conformance suite
/// expects. Results are converted to UTC (IEEE 9274.1.1, 4.2.7 "Timestamps").
/// </para>
/// <para>
/// Writing always uses UTC with a <c>Z</c> suffix and a fixed number of fraction digits: three, or seven when
/// the value has sub-millisecond ticks. Trimming trailing zeros makes some LRS conformance checks flaky
/// (ADL CTS issue #281), so they are never trimmed.
/// </para>
/// </remarks>
public static class XapiTimestamp
{
    /// <summary>Parses a timestamp and converts it to UTC.</summary>
    /// <param name="value">The timestamp text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <returns>The instant, with a zero offset.</returns>
    /// <exception cref="FormatException">The text is not a valid timestamp for <paramref name="version"/>.</exception>
    public static DateTimeOffset Parse(string value, XapiVersion version)
    {
        Guard.NotNull(value);
        return TryParse(value, version, out var result)
            ? result
            : throw new FormatException($"'{value}' is not an RFC 3339 timestamp.");
    }

    /// <summary>Tries to parse a timestamp and convert it to UTC.</summary>
    /// <param name="value">The timestamp text.</param>
    /// <param name="version">The xAPI version whose rules apply.</param>
    /// <param name="timestamp">The instant, with a zero offset, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid timestamp.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, XapiVersion version, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is null || value.Length < 20)
        {
            return false;
        }

        if (!TryDigits(value, 0, 4, out var year) || value[4] != '-' ||
            !TryDigits(value, 5, 2, out var month) || value[7] != '-' ||
            !TryDigits(value, 8, 2, out var day) ||
            !IsSeparator(value[10], version) ||
            !TryDigits(value, 11, 2, out var hour) || value[13] != ':' ||
            !TryDigits(value, 14, 2, out var minute) || value[16] != ':' ||
            !TryDigits(value, 17, 2, out var second))
        {
            return false;
        }

        var i = 19;
        long fractionTicks = 0;
        if (value[i] == '.')
        {
            i++;
            var start = i;
            var scale = TimeSpan.TicksPerSecond / 10;
            while (i < value.Length && XapiFormats.IsDigit(value[i]))
            {
                fractionTicks += (value[i] - '0') * scale;
                scale /= 10;
                i++;
            }

            if (i == start)
            {
                return false;
            }
        }

        if (i >= value.Length)
        {
            return false;
        }

        TimeSpan offset;
        if (value[i] is 'Z' or 'z')
        {
            if (i != value.Length - 1)
            {
                return false;
            }

            offset = TimeSpan.Zero;
        }
        else if (value[i] is '+' or '-' && value.Length == i + 6 &&
                 TryDigits(value, i + 1, 2, out var offsetHours) && value[i + 3] == ':' &&
                 TryDigits(value, i + 4, 2, out var offsetMinutes) && offsetHours <= 23 && offsetMinutes <= 59)
        {
            if (value[i] == '-' && offsetHours == 0 && offsetMinutes == 0)
            {
                return false;
            }

            offset = new TimeSpan(offsetHours, offsetMinutes, 0);
            if (value[i] == '-')
            {
                offset = offset.Negate();
            }
        }
        else
        {
            return false;
        }

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) ||
            hour > 23 || minute > 59 || second > 60)
        {
            return false;
        }

        // RFC 3339 allows a leap second (:60); represent it as the first instant of the next minute.
        var leap = second == 60;
        try
        {
            var local = new DateTime(year, month, day, hour, minute, leap ? 59 : second, DateTimeKind.Unspecified)
                .AddTicks(fractionTicks + (leap ? TimeSpan.TicksPerSecond : 0));
            timestamp = new DateTimeOffset(local, offset).ToUniversalTime();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Formats a timestamp in UTC as <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, or with seven fraction digits when the value
    /// has sub-millisecond ticks.
    /// </summary>
    /// <param name="value">The instant.</param>
    /// <returns>The RFC 3339 text.</returns>
    public static string Format(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var format = utc.Ticks % TimeSpan.TicksPerMillisecond == 0
            ? "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'"
            : "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff'Z'";
        return utc.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool IsSeparator(char c, XapiVersion version) => c is 'T' or 't' || (c == ' ' && version.IsV2);

    private static bool TryDigits(string value, int start, int length, out int result)
    {
        result = 0;
        if (start + length > value.Length)
        {
            return false;
        }

        for (var i = start; i < start + length; i++)
        {
            if (!XapiFormats.IsDigit(value[i]))
            {
                return false;
            }

            result = (result * 10) + (value[i] - '0');
        }

        return true;
    }
}
