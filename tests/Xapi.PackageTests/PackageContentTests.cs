using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Xapi.PackageTests;

public class PackageContentTests
{
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    public void Package_contains_readme_and_notices(string packageId)
    {
        var entries = EntryNames(packageId);

        Assert.Contains("README.md", entries);
        Assert.Contains("NOTICE", entries);
        Assert.Contains("THIRD-PARTY-NOTICES.md", entries);
        Assert.Contains("LICENSE", entries);
    }

    [Theory]
    [InlineData("Xapi.Core", "netstandard2.0")]
    [InlineData("Xapi.Core", "net10.0")]
    [InlineData("Xapi.Client", "netstandard2.0")]
    [InlineData("Xapi.Client", "net10.0")]
    public void Package_contains_assembly_and_xml_docs_for_target(string packageId, string targetFramework)
    {
        var entries = EntryNames(packageId);

        Assert.Contains($"lib/{targetFramework}/{packageId}.dll", entries);
        Assert.Contains($"lib/{targetFramework}/{packageId}.xml", entries);
    }

    [Theory]
    [InlineData("Xapi.Core", "netstandard2.0")]
    [InlineData("Xapi.Core", "net10.0")]
    [InlineData("Xapi.Client", "netstandard2.0")]
    [InlineData("Xapi.Client", "net10.0")]
    public void Packed_assembly_is_strong_name_signed(string packageId, string targetFramework)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        var entry = archive.GetEntry($"lib/{targetFramework}/{packageId}.dll");
        Assert.NotNull(entry);

        using var image = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(image);
        }

        image.Position = 0;
        using var pe = new PEReader(image);
        Assert.True(pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.StrongNameSigned), "The assembly is not strong-name signed.");

        var name = pe.GetMetadataReader().GetAssemblyDefinition().GetAssemblyName();
        Assert.Equal(PackageUnderTest.PublicKeyToken, PackageUnderTest.ToHex(name.GetPublicKeyToken() ?? []));
    }

    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    public void Nuspec_declares_license_readme_and_repository(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        var nuspecEntry = archive.GetEntry($"{packageId}.nuspec");
        Assert.NotNull(nuspecEntry);

        XDocument nuspec;
        using (var stream = nuspecEntry.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        var metadata = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;

        Assert.Equal(PackageUnderTest.Version, Value("version"));
        Assert.Equal("Apache-2.0", Value("license"));
        Assert.Equal("README.md", Value("readme"));
        Assert.False(string.IsNullOrWhiteSpace(Value("description")));

        var repository = metadata.Elements().Single(e => e.Name.LocalName == "repository");
        Assert.Equal("https://github.com/nurullahturkoglu/xapi-dotnet", repository.Attribute("url")?.Value);
    }

    // Microsoft.Extensions.Http is only for AddXapiClient, which exists on net10.0 alone (ADR-008)
    [Theory]
    [InlineData("netstandard2.0", false)]
    [InlineData("net10.0", true)]
    public void Client_package_depends_on_the_same_core_and_on_extensions_only_for_net10(string targetFramework, bool expectsExtensions)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath("Xapi.Client"));
        XDocument nuspec;
        using (var stream = archive.GetEntry("Xapi.Client.nuspec")!.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        // The nuspec spells the frameworks ".NETStandard2.0" and "net10.0"
        var group = nuspec.Descendants().Single(e => e.Name.LocalName == "group"
            && string.Equals(e.Attribute("targetFramework")?.Value.TrimStart('.'), targetFramework, StringComparison.OrdinalIgnoreCase));
        var declared = group.Elements().Where(e => e.Name.LocalName == "dependency").ToDictionary(
            e => e.Attribute("id")!.Value, e => e.Attribute("version")!.Value);

        Assert.Equal(PackageUnderTest.Version, declared["Xapi.Core"]);
        Assert.Equal(expectsExtensions, declared.ContainsKey("Microsoft.Extensions.Http"));
        if (!expectsExtensions)
        {
            Assert.DoesNotContain(declared.Keys, id => id.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        }
    }

    private static string[] EntryNames(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        return [.. archive.Entries.Select(e => e.FullName)];
    }
}
