using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xapi.Lrs.Storage;

/// <summary>Storage configuration for provider packages and custom stores.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
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

    /// <summary>
    /// Replaces the statement store with another implementation, for example one on a different database. The
    /// built-in store is removed, not wrapped: <typeparamref name="TStore"/> cannot receive it, so it implements
    /// every member itself.
    /// </summary>
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

    /// <summary>
    /// Replaces the document store with another implementation, for example one on a different database. The
    /// built-in store is removed, not wrapped: <typeparamref name="TStore"/> cannot receive it, so it implements
    /// every member itself.
    /// </summary>
    /// <typeparam name="TStore">The store type, resolved as a singleton.</typeparam>
    /// <param name="builder">The builder returned by <c>AddXapiLrs</c>.</param>
    /// <returns>The builder.</returns>
    public static IXapiLrsBuilder UseDocumentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IXapiLrsBuilder builder)
        where TStore : class, IDocumentStore
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IDocumentStore>();
        builder.Services.AddSingleton<IDocumentStore, TStore>();
        return builder;
    }
}
