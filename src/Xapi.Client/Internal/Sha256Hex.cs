using System.Security.Cryptography;

namespace Xapi.Client.Internal;

internal static class Sha256Hex
{
    public static string Compute(ReadOnlySpan<byte> data)
    {
#if NET
        return Convert.ToHexStringLower(SHA256.HashData(data));
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data.ToArray());
        var chars = new char[hash.Length * 2];
        const string Digits = "0123456789abcdef";
        for (var i = 0; i < hash.Length; i++)
        {
            chars[2 * i] = Digits[hash[i] >> 4];
            chars[(2 * i) + 1] = Digits[hash[i] & 0xF];
        }

        return new string(chars);
#endif
    }
}
