using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// An ISO 8601 duration as used by <c>result.duration</c>, for example <c>PT1H30M</c> or <c>P4W</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the format of ISO 8601:2004 4.4.3.2 is accepted (<c>PnYnMnDTnHnMnS</c> or <c>PnW</c>, a fraction only on
/// the last component); the alternative format of 4.4.3.3 (<c>P0001-02-03T…</c>) is rejected, as IEEE 9274.1.1,
/// 4.2.7 requires.
/// </para>
/// <para>
/// The original text is kept and written back unchanged. Equality compares component by component and ignores
/// precision beyond 0.01 second (IEEE 9274.1.1, 4.2.7 "Duration"). Components are not converted into each other,
/// so <c>PT60S</c> and <c>PT1M</c> are different, which matches the 1.0.3 rule that a duration is compared as a string.
/// </para>
/// </remarks>
public readonly struct XapiDuration : IEquatable<XapiDuration>
{
    private const string ZeroText = "PT0S";
    private static readonly Components Zero = new([0m, 0m, 0m, 0m, 0m, 0m, 0m]);

    private readonly string? _text;
    private readonly Components? _components;

    private XapiDuration(string text, Components components)
    {
        _text = text;
        _components = components;
    }

    private Components Parts => _components ?? Zero;

    /// <summary>The years component, or zero.</summary>
    public decimal Years => Parts.Values[0];

    /// <summary>The months component, or zero.</summary>
    public decimal Months => Parts.Values[1];

    /// <summary>The weeks component, or zero.</summary>
    public decimal Weeks => Parts.Values[2];

    /// <summary>The days component, or zero.</summary>
    public decimal Days => Parts.Values[3];

    /// <summary>The hours component, or zero.</summary>
    public decimal Hours => Parts.Values[4];

    /// <summary>The minutes component, or zero.</summary>
    public decimal Minutes => Parts.Values[5];

    /// <summary>The seconds component (with any fraction), or zero.</summary>
    public decimal Seconds => Parts.Values[6];

    /// <summary>Equality operator (0.01 second precision).</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the durations are equal.</returns>
    public static bool operator ==(XapiDuration left, XapiDuration right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the durations differ.</returns>
    public static bool operator !=(XapiDuration left, XapiDuration right) => !left.Equals(right);

    /// <summary>Parses an ISO 8601 duration.</summary>
    /// <param name="iso8601">The duration text.</param>
    /// <returns>The duration.</returns>
    /// <exception cref="FormatException">The text is not an ISO 8601 4.4.3.2 duration.</exception>
    public static XapiDuration Parse(string iso8601)
    {
        Guard.NotNull(iso8601);
        return TryParse(iso8601, out var duration)
            ? duration
            : throw new FormatException($"'{iso8601}' is not an ISO 8601 duration.");
    }

    /// <summary>Tries to parse an ISO 8601 duration.</summary>
    /// <param name="value">The duration text.</param>
    /// <param name="duration">The duration when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid duration.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out XapiDuration duration)
    {
        duration = default;
        if (value is null || value.Length < 3 || value[0] != 'P')
        {
            return false;
        }

        var values = new decimal[7];
        var i = 1;
        var inTime = false;
        var lastSlot = -1;
        var sawFraction = false;
        var componentsInTime = 0;
        var components = 0;
        var sawWeeks = false;
        while (i < value.Length)
        {
            if (value[i] == 'T')
            {
                if (inTime)
                {
                    return false;
                }

                inTime = true;
                i++;
                continue;
            }

            if (sawFraction)
            {
                return false;
            }

            var start = i;
            while (i < value.Length && XapiFormats.IsDigit(value[i]))
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }

            if (i < value.Length && value[i] == '.')
            {
                i++;
                var fractionStart = i;
                while (i < value.Length && XapiFormats.IsDigit(value[i]))
                {
                    i++;
                }

                if (i == fractionStart)
                {
                    return false;
                }

                sawFraction = true;
            }

            if (i >= value.Length)
            {
                return false;
            }

            var slot = SlotOf(value[i], inTime);
#if NET
            var digits = value.AsSpan(start, i - start);
#else
            var digits = value.Substring(start, i - start);
#endif
            if (slot <= lastSlot ||
                !decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            values[slot] = number;
            lastSlot = slot;
            sawWeeks |= slot == 2;
            components++;
            if (inTime)
            {
                componentsInTime++;
            }

            i++;
        }

        // P4W is complete on its own; ISO 8601 4.4.3.2 does not combine weeks with other components.
        if (components == 0 || (inTime && componentsInTime == 0) || (sawWeeks && components > 1))
        {
            return false;
        }

        duration = new XapiDuration(value, new Components(values));
        return true;
    }

    /// <summary>
    /// Creates a duration from a <see cref="TimeSpan"/>, as <c>PnDTnHnMn.nnS</c> with zero components left out and
    /// seconds truncated to 0.01 (the precision IEEE 9274.1.1 asks Learning Record Providers to send at most).
    /// </summary>
    /// <param name="value">A non-negative time span.</param>
    /// <returns>The duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static XapiDuration FromTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A duration must not be negative.");
        }

        var builder = new StringBuilder("P");
        if (value.Days > 0)
        {
            builder.Append(value.Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        }

        var hundredths = (value.Ticks % TimeSpan.TicksPerSecond) / (TimeSpan.TicksPerMillisecond * 10);
        if (value.Hours > 0 || value.Minutes > 0 || value.Seconds > 0 || hundredths > 0 || value.Days == 0)
        {
            builder.Append('T');
            if (value.Hours > 0)
            {
                builder.Append(value.Hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            }

            if (value.Minutes > 0)
            {
                builder.Append(value.Minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            }

            if (value.Seconds > 0 || hundredths > 0 || (value.Hours == 0 && value.Minutes == 0))
            {
                builder.Append(value.Seconds.ToString(CultureInfo.InvariantCulture));
                if (hundredths > 0)
                {
                    builder.Append('.').Append(hundredths.ToString("00", CultureInfo.InvariantCulture).TrimEnd('0'));
                }

                builder.Append('S');
            }
        }

        return Parse(builder.ToString());
    }

    /// <summary>
    /// Converts to a <see cref="TimeSpan"/> using nominal lengths: a year is 365 days and a month is 30 days.
    /// The xAPI standard defines no conversion, so use this for display or rough arithmetic only.
    /// </summary>
    /// <returns>The time span.</returns>
    /// <exception cref="OverflowException">The duration does not fit in a <see cref="TimeSpan"/>.</exception>
    public TimeSpan ToTimeSpan()
    {
        var totalSeconds =
            (Years * 365m * 86400m) + (Months * 30m * 86400m) + (Weeks * 7m * 86400m) + (Days * 86400m) +
            (Hours * 3600m) + (Minutes * 60m) + Seconds;
        var ticks = decimal.Truncate(totalSeconds * TimeSpan.TicksPerSecond);
        if (ticks > TimeSpan.MaxValue.Ticks)
        {
            throw new OverflowException("The duration does not fit in a TimeSpan.");
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>Returns the original duration text (<c>PT0S</c> for the default value).</summary>
    /// <returns>The ISO 8601 text.</returns>
    public override string ToString() => _text ?? ZeroText;

    /// <inheritdoc/>
    public bool Equals(XapiDuration other)
    {
        var left = Parts.Values;
        var right = other.Parts.Values;
        for (var i = 0; i < 6; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return TruncateToHundredths(left[6]) == TruncateToHundredths(right[6]);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XapiDuration other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var values = Parts.Values;
        var hash = 0;
        for (var i = 0; i < 6; i++)
        {
            hash = Hash.Combine(hash, decimal.Truncate(values[i] * 1000m).GetHashCode());
        }

        return Hash.Combine(hash, (TruncateToHundredths(values[6]) * 100m).GetHashCode());
    }

    private static decimal TruncateToHundredths(decimal seconds) => decimal.Truncate(seconds * 100m) / 100m;

    private static int SlotOf(char designator, bool inTime) =>
        (designator, inTime) switch
        {
            ('Y', false) => 0,
            ('M', false) => 1,
            ('W', false) => 2,
            ('D', false) => 3,
            ('H', true) => 4,
            ('M', true) => 5,
            ('S', true) => 6,
            _ => -1,
        };

    private sealed class Components(decimal[] values)
    {
        public decimal[] Values { get; } = values;
    }
}
