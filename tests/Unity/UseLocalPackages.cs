// Points the Unity test project at Xapi packages packed from this checkout, so CI tests the code under review
// rather than the version on nuget.org. Run it before `dotnet nugetforunity restore`:
//   dotnet pack src/Xapi.Client.Outbox -c Release -o artifacts/packages
//   dotnet run tests/Unity/UseLocalPackages.cs -- artifacts/packages
// NuGetForUnity's restore installs exactly what Assets/packages.config lists, without resolving dependencies, so
// this fails when a package needs a newer dependency than the file lists; update it from the NuGet window then.

using System.IO.Compression;
using System.Xml.Linq;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run tests/Unity/UseLocalPackages.cs -- <directory with the Xapi nupkgs>");
    return 2;
}

var packageDirectory = Path.GetFullPath(args[0]);
var projectDirectory = Path.Combine(Path.GetDirectoryName(GetSourcePath())!, "Assets");
var packagesConfigPath = Path.Combine(projectDirectory, "packages.config");
var nugetConfigPath = Path.Combine(projectDirectory, "NuGet.config");

var packagesConfig = XDocument.Load(packagesConfigPath);
var listed = packagesConfig.Root!.Elements("package").ToDictionary(p => (string)p.Attribute("id")!, StringComparer.OrdinalIgnoreCase);

var errors = new List<string>();
foreach (var package in listed.Values.Where(p => ((string)p.Attribute("id")!).StartsWith("Xapi.", StringComparison.Ordinal)))
{
    var id = (string)package.Attribute("id")!;
    var nupkgs = Directory.GetFiles(packageDirectory, id + ".*.nupkg")
        .Where(f => char.IsAsciiDigit(Path.GetFileName(f)[id.Length + 1]))
        .ToList();
    if (nupkgs.Count != 1)
    {
        errors.Add($"Expected one {id} package in {packageDirectory}, found {nupkgs.Count}.");
        continue;
    }

    using var archive = ZipFile.OpenRead(nupkgs[0]);
    using var nuspecStream = archive.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
    var nuspec = XDocument.Load(nuspecStream);
    var ns = nuspec.Root!.Name.Namespace;
    var metadata = nuspec.Root.Element(ns + "metadata")!;
    package.SetAttributeValue("version", (string)metadata.Element(ns + "version")!);

    var group = metadata.Element(ns + "dependencies")!.Elements(ns + "group")
        .Single(g => (string?)g.Attribute("targetFramework") == ".NETStandard2.0");
    foreach (var dependency in group.Elements(ns + "dependency"))
    {
        var dependencyId = (string)dependency.Attribute("id")!;
        if (dependencyId.StartsWith("Xapi.", StringComparison.Ordinal))
        {
            continue;
        }

        // Only the lower bound matters here: "[10.0.0, )" or "10.0.0"
        var minimum = Version.Parse(((string)dependency.Attribute("version")!).Trim('[', '(').Split(',')[0].Trim());
        if (!listed.TryGetValue(dependencyId, out var entry))
        {
            errors.Add($"{id} depends on {dependencyId} {minimum}, which Assets/packages.config does not list.");
        }
        else if (Version.Parse((string)entry.Attribute("version")!) < minimum)
        {
            errors.Add($"{id} needs {dependencyId} {minimum} or later; Assets/packages.config has {(string)entry.Attribute("version")!}.");
        }
    }
}

if (errors.Count > 0)
{
    errors.ForEach(e => Console.Error.WriteLine("error: " + e));
    return 1;
}

packagesConfig.Save(packagesConfigPath);

var nugetConfig = XDocument.Load(nugetConfigPath);
nugetConfig.Root!.Element("packageSources")!.Add(new XElement("add", new XAttribute("key", "local"), new XAttribute("value", packageDirectory)));
nugetConfig.Save(nugetConfigPath);

foreach (var package in listed.Values.Where(p => ((string)p.Attribute("id")!).StartsWith("Xapi.", StringComparison.Ordinal)))
{
    Console.WriteLine($"{package.Attribute("id")!.Value} {package.Attribute("version")!.Value} from {packageDirectory}");
}

return 0;

static string GetSourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
