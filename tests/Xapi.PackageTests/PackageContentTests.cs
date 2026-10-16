using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Xapi.PackageTests;

public class PackageContentTests
{
    [Theory]
    [InlineData("Xapi.Core")]
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
    public void Package_contains_assembly_and_xml_docs_for_target(string packageId, string targetFramework)
    {
        var entries = EntryNames(packageId);

        Assert.Contains($"lib/{targetFramework}/{packageId}.dll", entries);
        Assert.Contains($"lib/{targetFramework}/{packageId}.xml", entries);
    }

    [Theory]
    [InlineData("Xapi.Core", "netstandard2.0")]
    [InlineData("Xapi.Core", "net10.0")]
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

    private static string[] EntryNames(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        return [.. archive.Entries.Select(e => e.FullName)];
    }
}
