using System.Globalization;
using System.Reflection;

namespace Xapi.PackageTests;

internal static class PackageUnderTest
{
    // Public key token of eng/xapi-dotnet.snk
    public const string PublicKeyToken = "abaa9ebe686c5f55";

    public static string Version { get; } = Metadata("XapiPackageVersion");

    // The dependency floors of the netstandard2.0 builds, as in Directory.Packages.props. Repeated here so that a
    // floor only moves on purpose (CONTRIBUTING.md, "Dependency floors").
    public static IReadOnlyDictionary<string, string> DependencyFloors { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Microsoft.Bcl.AsyncInterfaces"] = "10.0.0",
        ["Microsoft.Bcl.TimeProvider"] = "8.0.1",
        ["System.Diagnostics.DiagnosticSource"] = "7.0.0",
        ["System.Text.Json"] = "10.0.0",
    };

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
