using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xapi.Lrs.Storage;

/// <summary>Storage configuration for provider packages and custom stores.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public static class XapiLrsBuilderStorageExtensions
{
    /// <summary>Sets the database the LRS stores data in, replacing an earlier choice.</summary>
    /// <param name="builder">The builder returned by <c>AddXapiLrs</c>.</param>
    /// <param name="provider">The database provider.</param>
    /// <returns>The builder.</returns>
    public static IXapiLrsBuilder UseDatabaseProvider(this IXapiLrsBuilder builder, XapiLrsDatabaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        builder.Services.RemoveAll<XapiLrsDatabaseProvider>();
        builder.Services.AddSingleton(provider);
        return builder;
    }

    /// <summary>Replaces the statement store, for example with a decorator or a different database.</summary>
    /// <typeparam name="TStore">The store type, resolved as a singleton.</typeparam>
    /// <param name="builder">The builder returned by <c>AddXapiLrs</c>.</param>
    /// <returns>The builder.</returns>
    public static IXapiLrsBuilder UseStatementStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IXapiLrsBuilder builder)
        where TStore : class, IStatementStore
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IStatementStore>();
        builder.Services.AddSingleton<IStatementStore, TStore>();
        return builder;
    }
}
