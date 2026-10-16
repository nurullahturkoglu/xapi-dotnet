using System.Reflection;
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
    public void Public_API_matches_the_approved_snapshot(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        ApiSnapshot.Match(assembly.GeneratePublicApi(Options), assemblyName);
    }
}
