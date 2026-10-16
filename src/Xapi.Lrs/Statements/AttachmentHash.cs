using System.Security.Cryptography;

namespace Xapi.Lrs.Statements;

// The sha2 of an Attachment is "the SHA-2 (FIPS PUB 180-2) hash of the Attachment data" (IEEE 9274.1.1,
// 4.2.2.6), without naming the digest size. The size is read from the length of the declared hash, so
// SHA-256, SHA-384 and SHA-512 are recognized; SHA-224 has no .NET implementation.
internal static class AttachmentHash
{
    public static bool IsSupported(string sha2) =>
        sha2.Length is 64 or 96 or 128 && sha2.All(char.IsAsciiHexDigit);

    // The hash of the data in the size of the declared hash, as lower-case hex, or null for an unsupported one
    public static string? Compute(string declared, ReadOnlySpan<byte> data) => declared.Length switch
    {
        _ when !IsSupported(declared) => null,
        64 => Convert.ToHexStringLower(SHA256.HashData(data)),
        96 => Convert.ToHexStringLower(SHA384.HashData(data)),
        _ => Convert.ToHexStringLower(SHA512.HashData(data)),
    };

    public static string Normalize(string sha2) => sha2.ToLowerInvariant();
}
