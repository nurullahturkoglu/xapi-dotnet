namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Configures the LRS added with <c>AddXapiLrs</c>, for example with <c>UseSqlite</c>.</summary>
public interface IXapiLrsBuilder
{
    /// <summary>The application's services.</summary>
    IServiceCollection Services { get; }
}
