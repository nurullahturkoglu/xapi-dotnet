namespace Xapi.Client.Outbox.Internal;

/// <summary>CRC-32 (IEEE 802.3, reflected polynomial 0xEDB88320), the checksum of zip and PNG.</summary>
/// <remarks>System.IO.Hashing would need another package on netstandard2.0 for one small function.</remarks>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB8_8320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
