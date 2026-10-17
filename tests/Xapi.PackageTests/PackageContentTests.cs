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
    [InlineData("Xapi.Client.Outbox")]
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
    public void Package_contains_readme_icon_and_notices(string packageId)
    {
        var entries = EntryNames(packageId);

        Assert.Contains("README.md", entries);
        Assert.Contains("icon.png", entries);
        Assert.Contains("NOTICE", entries);
        Assert.Contains("THIRD-PARTY-NOTICES.md", entries);
        Assert.Contains("LICENSE", entries);
    }

    // Each package ships its own short README, not the repository README
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    [InlineData("Xapi.Client.Outbox")]
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
    public void Package_readme_is_the_package_own(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        using var reader = new StreamReader(archive.GetEntry("README.md")!.Open());

        Assert.Equal($"# {packageId}", reader.ReadLine());
    }

    [Theory]
    [InlineData("Xapi.Core", "netstandard2.0")]
    [InlineData("Xapi.Core", "net10.0")]
    [InlineData("Xapi.Client", "netstandard2.0")]
    [InlineData("Xapi.Client", "net10.0")]
    [InlineData("Xapi.Client.Outbox", "netstandard2.0")]
    [InlineData("Xapi.Client.Outbox", "net10.0")]
    [InlineData("Xapi.Lrs", "net10.0")]
    [InlineData("Xapi.Lrs.Sqlite", "net10.0")]
    [InlineData("Xapi.Lrs.PostgreSql", "net10.0")]
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
    [InlineData("Xapi.Client.Outbox", "netstandard2.0")]
    [InlineData("Xapi.Client.Outbox", "net10.0")]
    [InlineData("Xapi.Lrs", "net10.0")]
    [InlineData("Xapi.Lrs.Sqlite", "net10.0")]
    [InlineData("Xapi.Lrs.PostgreSql", "net10.0")]
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
    [InlineData("Xapi.Client.Outbox")]
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
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
        Assert.Equal("icon.png", Value("icon"));
        Assert.False(string.IsNullOrWhiteSpace(Value("description")));

        var repository = metadata.Elements().Single(e => e.Name.LocalName == "repository");
        Assert.Equal("https://github.com/nurullahturkoglu/xapi-dotnet", repository.Attribute("url")?.Value);
    }

    [Theory]
    [InlineData("Xapi.Core", "statements json validation")]
    [InlineData("Xapi.Client", "client httpclient unity")]
    [InlineData("Xapi.Client.Outbox", "outbox offline retry")]
    [InlineData("Xapi.Lrs", "lrs learning-record-store aspnetcore")]
    [InlineData("Xapi.Lrs.Sqlite", "lrs efcore sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql", "lrs efcore postgresql")]
    public void Nuspec_has_the_shared_and_package_tags_and_links_the_changelog(string packageId, string packageTags)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        XDocument nuspec;
        using (var stream = archive.GetEntry($"{packageId}.nuspec")!.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        var metadata = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value;

        Assert.Equal($"xapi experience-api tincan elearning ieee-9274 {packageTags}", Value("tags"));
        Assert.Equal("https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CHANGELOG.md", Value("releaseNotes"));
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
            // Unity and .NET Framework apps ship each of these as a DLL. System.Text.Json is listed because central
            // transitive pinning pins the version Xapi.Core brings; DiagnosticSource is for Meter (ADR-014).
            Assert.Equal(
                ["Microsoft.Bcl.AsyncInterfaces", "System.Diagnostics.DiagnosticSource", "System.Text.Json", "Xapi.Core"],
                declared.Keys.Order(StringComparer.Ordinal));
        }
    }

    // A netstandard2.0 dependency is a minimum version for every .NET Framework and Unity app, so it is the lowest
    // version that has the APIs used and no known vulnerability, not the newest one
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    [InlineData("Xapi.Client.Outbox")]
    public void NetStandard_dependencies_are_the_floor_versions(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        XDocument nuspec;
        using (var stream = archive.GetEntry($"{packageId}.nuspec")!.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        var group = nuspec.Descendants().Single(e => e.Name.LocalName == "group" && e.Attribute("targetFramework")?.Value == ".NETStandard2.0");
        var dependencies = group.Elements()
            .Where(e => e.Name.LocalName == "dependency" && !e.Attribute("id")!.Value.StartsWith("Xapi.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(dependencies);
        Assert.All(dependencies, d =>
        {
            var id = d.Attribute("id")!.Value;
            Assert.True(PackageUnderTest.DependencyFloors.TryGetValue(id, out var floor), $"{id} has no floor in Directory.Packages.props.");
            Assert.Equal(floor, d.Attribute("version")?.Value);
        });
    }

    // The providers bind to the experimental storage types of Xapi.Lrs, which can change in any release, so a
    // provider must never be resolved against a different Xapi.Lrs than the one it was built with
    [Theory]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
    public void Lrs_provider_packages_depend_on_the_exact_lrs_version(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        XDocument nuspec;
        using (var stream = archive.GetEntry($"{packageId}.nuspec")!.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        var lrs = nuspec.Descendants().Single(e => e.Name.LocalName == "dependency" && e.Attribute("id")?.Value == "Xapi.Lrs");
        Assert.Equal($"[{PackageUnderTest.Version}]", lrs.Attribute("version")?.Value);
    }

    [Theory]
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
    public void Lrs_packages_target_only_net10(string packageId)
    {
        var entries = EntryNames(packageId);

        Assert.Equal(["net10.0"], entries.Where(e => e.StartsWith("lib/", StringComparison.Ordinal)).Select(e => e.Split('/')[1]).Distinct());
    }

    [Fact]
    public void Lrs_package_declares_the_asp_net_core_framework_reference()
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath("Xapi.Lrs"));
        using var stream = archive.GetEntry("Xapi.Lrs.nuspec")!.Open();
        var frameworkReferences = XDocument.Load(stream).Descendants().Where(e => e.Name.LocalName == "frameworkReference");
        Assert.Contains(frameworkReferences, e => e.Attribute("name")?.Value == "Microsoft.AspNetCore.App");
    }

    private static string[] EntryNames(string packageId)
    {
        using var archive = ZipFile.OpenRead(PackageUnderTest.NupkgPath(packageId));
        return [.. archive.Entries.Select(e => e.FullName)];
    }
}
