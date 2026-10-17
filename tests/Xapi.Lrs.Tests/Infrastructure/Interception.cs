using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Tests.Infrastructure;

// EF Core interceptors on the LRS's database, for tests that look at the SQL it runs.
internal static class Interception
{
    // Replaces the configured database provider with one that adds the interceptors
    public static void Add(IServiceCollection services, params IInterceptor[] interceptors)
    {
        var inner = (XapiLrsDatabaseProvider)services.Single(d => d.ServiceType == typeof(XapiLrsDatabaseProvider)).ImplementationInstance!;
        services.Replace(ServiceDescriptor.Singleton<XapiLrsDatabaseProvider>(new InterceptingProvider(inner, interceptors)));
    }

    private sealed class InterceptingProvider(XapiLrsDatabaseProvider inner, IInterceptor[] interceptors) : XapiLrsDatabaseProvider
    {
        public override string Name => inner.Name;

        public override void Configure(DbContextOptionsBuilder optionsBuilder)
        {
            inner.Configure(optionsBuilder);
            optionsBuilder.AddInterceptors(interceptors);
        }

        public override Task<IDbContextTransaction> BeginWriteTransactionAsync(DbContext context, CancellationToken cancellationToken) =>
            inner.BeginWriteTransactionAsync(context, cancellationToken);

        public override bool IsTransientWriteFailure(Exception exception) => inner.IsTransientWriteFailure(exception);
    }
}
