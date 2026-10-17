using System.Buffers;
using System.Buffers.Text;

namespace Xapi.Lrs.Storage;

// Storage keys are monotonic UUIDv7 whose 48-bit time part is the stored time in Unix milliseconds (ADR-020),
// so a stored-time bound is a key bound. The smallest key of a millisecond has that time part and every other
// bit zero; it sorts before every real key of that millisecond, which all carry version bits.
internal static class StatementKeys
{
    // Keys of statements stored after `since` (exclusive) are at least this. Stored times have whole
    // milliseconds, so "stored > since" is "stored millisecond > floor(since)".
    public static Guid FirstAfter(DateTimeOffset since) => FirstOf(Milliseconds(since) + 1);

    public static Guid FirstOf(long unixMilliseconds)
    {
        var ms = Math.Clamp(unixMilliseconds, 0, (1L << 48) - 1);
        return new Guid(unchecked((int)(ms >> 16)), unchecked((short)(ms & 0xFFFF)), 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    // The continuation token of a page that ends at `key`: its 16 bytes in RFC 9562 order, base64url-encoded
    // (22 characters). Opaque to the LRS and its clients; only this store reads it back.
    public static string ToContinuationToken(Guid key)
    {
        Span<byte> bytes = stackalloc byte[16];
        key.TryWriteBytes(bytes, bigEndian: true, out _);
        return Base64Url.EncodeToString(bytes);
    }

    public static bool TryReadContinuationToken(string token, out Guid key)
    {
        Span<byte> bytes = stackalloc byte[16];
        // DecodeFromChars reports invalid characters as a status; TryDecodeFromChars throws for them
        if (token.Length == 22 &&
            Base64Url.DecodeFromChars(token, bytes, out _, out var written) == OperationStatus.Done &&
            written == 16)
        {
            key = new Guid(bytes, bigEndian: true);
            return true;
        }

        key = default;
        return false;
    }

    private static long Milliseconds(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var milliseconds = ticks / TimeSpan.TicksPerMillisecond;
        return ticks < 0 && ticks % TimeSpan.TicksPerMillisecond != 0 ? milliseconds - 1 : milliseconds;
    }
}
