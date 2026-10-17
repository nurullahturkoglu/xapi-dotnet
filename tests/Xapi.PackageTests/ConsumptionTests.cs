using System.Reflection;

namespace Xapi.PackageTests;

public class ConsumptionTests
{
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    [InlineData("Xapi.Client.Outbox")]
#if NET
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
#endif
    public void Referenced_assembly_comes_from_the_package_under_test(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informationalVersion);
        Assert.Equal(PackageUnderTest.Version, informationalVersion.Split('+')[0]);

        Assert.Equal(PackageUnderTest.PublicKeyToken, PackageUnderTest.ToHex(assembly.GetName().GetPublicKeyToken() ?? []));
    }

#if NETFRAMEWORK
    // Directory.Packages.props pins the floors, so here the packages run on the oldest dependencies their nuspecs allow
    [Theory]
    [InlineData(typeof(IAsyncDisposable), "Microsoft.Bcl.AsyncInterfaces")]
    [InlineData(typeof(TimeProvider), "Microsoft.Bcl.TimeProvider")]
    [InlineData(typeof(System.Diagnostics.Metrics.Meter), "System.Diagnostics.DiagnosticSource")]
    [InlineData(typeof(System.Text.Json.JsonDocument), "System.Text.Json")]
    public void Dependencies_are_loaded_at_the_floor_versions(Type type, string packageId)
    {
        var informationalVersion = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informationalVersion);
        Assert.Equal(PackageUnderTest.DependencyFloors[packageId], informationalVersion.Split('+')[0]);
    }
#endif
}
