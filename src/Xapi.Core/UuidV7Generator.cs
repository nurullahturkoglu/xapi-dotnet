using System.Security.Cryptography;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// Generates RFC 9562 version 7 UUIDs that are strictly increasing per generator, also within the same
/// millisecond. Use them as statement ids and as time-ordered storage keys.
/// </summary>
/// <remarks>
/// <para>
/// Layout (RFC 9562, 6.2 method 1, fixed-length dedicated counter): 48-bit Unix time in milliseconds, version
/// 7, then a 42-bit counter spread over <c>rand_a</c> and the top of <c>rand_b</c>, then 32 random bits. A new
/// millisecond reseeds the counter with a random value whose top bit is clear, leaving room to increment. If
/// the counter overflows, or the clock goes backwards, the timestamp field moves forward by one millisecond
/// so the ordering never breaks.
/// </para>
/// <para>
/// <c>Guid.CreateVersion7()</c> on .NET 9+ fills the same bits randomly, so ids created in one millisecond do not
/// sort in creation order; this generator exists for that reason and also runs on netstandard2.0.
/// The ordering holds for the canonical string form and for big-endian bytes, not for <see cref="Guid.ToByteArray()"/>.
/// </para>
/// </remarks>
public sealed class UuidV7Generator
{
    private const int CounterBits = 42;
    private const long CounterMax = (1L << CounterBits) - 1;

    private readonly Func<DateTimeOffset> _clock;
    private static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();

    private readonly byte[] _buffer = new byte[10];
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private long _lastMilliseconds = -1;
    private long _counter;

    /// <summary>Creates a generator that reads the system clock.</summary>
    public UuidV7Generator()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Creates a generator that reads the given clock.</summary>
    /// <param name="clock">Returns the current time.</param>
    public UuidV7Generator(Func<DateTimeOffset> clock) => _clock = Guard.NotNull(clock);

    /// <summary>A process-wide generator using the system clock. Thread-safe.</summary>
    public static UuidV7Generator Shared { get; } = new();

    /// <summary>Creates the next UUID. Thread-safe.</summary>
    /// <returns>A version 7, variant 2 UUID greater than every UUID this generator returned before.</returns>
    public Guid NewGuid()
    {
        long milliseconds;
        long counter;
        int tail;
        lock (_gate)
        {
            Random.GetBytes(_buffer);
            var now = _clock().ToUnixTimeMilliseconds();
            if (now > _lastMilliseconds)
            {
                _lastMilliseconds = now;
                _counter = ReadSeed();
            }
            else if (++_counter > CounterMax)
            {
                _lastMilliseconds++;
                _counter = ReadSeed();
            }

            milliseconds = _lastMilliseconds;
            counter = _counter;
            tail = BitConverter.ToInt32(_buffer, 6);
        }

        var randA = (short)(0x7000 | (int)((counter >> 30) & 0x0FFF));
        var low30 = (int)(counter & 0x3FFF_FFFF);
        return new Guid(
            (int)(milliseconds >> 16),
            unchecked((short)(milliseconds & 0xFFFF)),
            randA,
            (byte)(0x80 | ((low30 >> 24) & 0x3F)),
            (byte)(low30 >> 16),
            (byte)(low30 >> 8),
            (byte)low30,
            (byte)(tail >> 24),
            (byte)(tail >> 16),
            (byte)(tail >> 8),
            (byte)tail);
    }

    /// <summary>Reads the Unix millisecond timestamp stored in a version 7 UUID.</summary>
    /// <param name="uuid">A version 7 UUID.</param>
    /// <returns>The embedded time, in UTC.</returns>
    /// <exception cref="ArgumentException"><paramref name="uuid"/> is not a version 7 UUID.</exception>
    public static DateTimeOffset GetTimestamp(Guid uuid)
    {
        var text = uuid.ToString("N");
        if (text[12] != '7')
        {
            throw new ArgumentException("The UUID is not version 7.", nameof(uuid));
        }

        var milliseconds = Convert.ToInt64(text.Substring(0, 12), 16);
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private long ReadSeed()
    {
        long seed = 0;
        for (var i = 0; i < 6; i++)
        {
            seed = (seed << 8) | _buffer[i];
        }

        return seed & (CounterMax >> 1);
    }
}
