using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using PublicApiGenerator;

namespace Xapi.PublicApi.Tests;

public class PublicApiTests
{
    private static readonly ApiGeneratorOptions Options = new()
    {
        IncludeAssemblyAttributes = false,
        // Being a record (value equality, with-expressions) is part of the contract
        TreatRecordsAsClasses = false,
        // The default hides System.* and Microsoft.*, where DI extension methods conventionally live
        DenyNamespacePrefixes = [],
        ExcludeAttributes =
        [
            "System.Diagnostics.DebuggerBrowsableAttribute",
            "System.Diagnostics.DebuggerDisplayAttribute",
            "System.Diagnostics.DebuggerStepThroughAttribute",
        ],
    };

    // One row per shipped assembly. Loaded by name so this project does not depend on any particular type.
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    [InlineData("Xapi.Client.Outbox")]
    [InlineData("Xapi.Lrs")]
    [InlineData("Xapi.Lrs.Sqlite")]
    [InlineData("Xapi.Lrs.PostgreSql")]
    public void Public_API_matches_the_approved_snapshot(string assemblyName)
    {
        var path = Assembly.Load(assemblyName).Location;

        ApiSnapshot.Match(GeneratePublicApi(path, assemblyName + "-net10.0"), assemblyName);
    }

    // The netstandard2.0 build has a public API of its own: an #if for one target framework can add or drop a member
    [Theory]
    [InlineData("Xapi.Core")]
    [InlineData("Xapi.Client")]
    [InlineData("Xapi.Client.Outbox")]
    public void NetStandard_public_API_matches_the_approved_snapshot(string assemblyName)
    {
        var path = typeof(PublicApiTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "netstandard2.0:" + assemblyName)
            .Value!;

        ApiSnapshot.Match(GeneratePublicApi(path, assemblyName + "-netstandard2.0"), assemblyName + ".netstandard2.0");
    }

    // PublicApiGenerator leaves out members marked [CompilerGenerated]. On records that hides what the record keyword
    // already implies (Equals, GetHashCode, ToString, == and !=, EqualityContract, PrintMembers, <Clone>$ and the copy
    // constructor), and one member it does not: the Deconstruct of a positional record, whose signature follows the
    // record's parameters. The generator reads a copy of the assembly without that attribute on Deconstruct.
    private static string GeneratePublicApi(string assemblyPath, string label)
    {
        var directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "api-snapshot-input", label));
        var copyPath = Path.Combine(directory.FullName, Path.GetFileName(assemblyPath));

        using (var module = ModuleDefinition.ReadModule(assemblyPath))
        {
            foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.Name == "Deconstruct"))
            {
                var generated = method.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
                if (generated is not null)
                {
                    method.CustomAttributes.Remove(generated);
                }
            }

            module.Write(copyPath);
        }

        // The generator reads the file at Assembly.Location; a context of its own keeps the copy apart from the
        // assemblies this test project references
        var context = new AssemblyLoadContext(label, isCollectible: true);
        try
        {
            return context.LoadFromAssemblyPath(copyPath).GeneratePublicApi(Options);
        }
        finally
        {
            context.Unload();
        }
    }
}
