using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Xapi.Lrs.Storage;

/// <summary>
/// Connects <see cref="XapiLrsDbContext"/> to a database. Implemented by the provider packages
/// (<c>UseSqlite</c>, <c>UsePostgreSql</c>) and registered with <c>UseDatabaseProvider</c>.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public abstract class XapiLrsDatabaseProvider
{
    /// <summary>A short name for logs and errors, such as <c>SQLite</c>.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Configures the EF Core provider, its migrations assembly and the migrations history table
    /// (<see cref="XapiLrsDbContext.MigrationsHistoryTable"/>).
    /// </summary>
    /// <param name="optionsBuilder">The options of the context.</param>
    public abstract void Configure(DbContextOptionsBuilder optionsBuilder);

    /// <summary>
    /// Starts the transaction that a statement batch is written in. It must behave as serializable for the
    /// rows the write reads and writes, so that concurrent writers of the same statement id or activity
    /// cannot both succeed on stale reads.
    /// </summary>
    /// <param name="context">The context to start the transaction on.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The transaction.</returns>
    public abstract Task<IDbContextTransaction> BeginWriteTransactionAsync(DbContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a write failed only because of a concurrent write (a serialization failure, a lock timeout or a
    /// unique key violation), so that running the whole write again can succeed.
    /// </summary>
    /// <param name="exception">The exception thrown by the write.</param>
    /// <returns><see langword="true"/> when the write should be retried.</returns>
    public abstract bool IsTransientWriteFailure(Exception exception);
}
