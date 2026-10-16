namespace Xapi.Internal;

// System.HashCode is not available on netstandard2.0 without an extra package.
internal static class Hash
{
    public static int Combine(int seed, int value) => unchecked((seed * 31) + value);

    public static int Of<T>(T value) => value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value);
}
