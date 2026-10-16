using System.Reflection;

namespace Xapi.PackageTests;

public class ConsumptionTests
{
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    public void Referenced_assembly_comes_from_the_package_under_test(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informationalVersion);
        Assert.Equal(PackageUnderTest.Version, informationalVersion.Split('+')[0]);

        Assert.Equal(PackageUnderTest.PublicKeyToken, PackageUnderTest.ToHex(assembly.GetName().GetPublicKeyToken() ?? []));
    }
}
