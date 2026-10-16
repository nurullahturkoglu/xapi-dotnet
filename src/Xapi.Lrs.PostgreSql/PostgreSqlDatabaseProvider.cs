using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.PostgreSql;

internal sealed class PostgreSqlDatabaseProvider(string connectionString) : XapiLrsDatabaseProvider
{
    public override string Name => "PostgreSQL";

    public override void Configure(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsAssembly(typeof(PostgreSqlDatabaseProvider).Assembly.GetName().Name)
            .MigrationsHistoryTable(XapiLrsDbContext.MigrationsHistoryTable));
    }

    // SERIALIZABLE turns the write skews a statement batch can hit (a voiding statement and its target stored
    // concurrently, two merges of one activity definition) into serialization failures, which are retried.
    public override async Task<IDbContextTransaction> BeginWriteTransactionAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
    }

    public override bool IsTransientWriteFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation,
                })
            {
                return true;
            }
        }

        return false;
    }
}

// Used by dotnet-ef when generating migrations (eng/ef-migrations.sh); no database is opened.
internal sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<XapiLrsDbContext>
{
    public XapiLrsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<XapiLrsDbContext>();
        new PostgreSqlDatabaseProvider("Host=localhost;Database=xapi_design_time").Configure(options);
        return new XapiLrsDbContext(options.Options);
    }
}
