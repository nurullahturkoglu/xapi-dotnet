using System.Globalization;
using System.Reflection;

namespace Xapi.PackageTests;

internal static class PackageUnderTest
{
    // Public key token of eng/xapi-dotnet.snk
    public const string PublicKeyToken = "abaa9ebe686c5f55";

    public static string Version { get; } = Metadata("XapiPackageVersion");

    public static string Feed { get; } = Metadata("XapiPackageFeed");

    public static string NupkgPath(string packageId)
    {
        var path = Path.Combine(Feed, $"{packageId}.{Version}.nupkg");
        Assert.True(File.Exists(path), $"{path} does not exist. Pack the solution into {Feed} with the same version first.");
        return path;
    }

    public static string ToHex(byte[] bytes) =>
        string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    private static string Metadata(string key) => typeof(PackageUnderTest).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == key)
        .Value!;
}
