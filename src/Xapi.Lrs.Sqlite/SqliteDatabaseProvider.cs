using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Sqlite;

internal sealed class SqliteDatabaseProvider(string connectionString) : XapiLrsDatabaseProvider
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    public override string Name => "SQLite";

    public override void Configure(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder
            .UseSqlite(connectionString, sqlite => sqlite
                .MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name)
                .MigrationsHistoryTable(XapiLrsDbContext.MigrationsHistoryTable))
            .AddInterceptors(SqlitePragmas.Instance);
    }

    // Microsoft.Data.Sqlite starts a serializable transaction with BEGIN IMMEDIATE: the write lock is taken up
    // front, so two writers never both read and then fail to upgrade to a write lock (SQLITE_BUSY mid-transaction).
    public override async Task<IDbContextTransaction> BeginWriteTransactionAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
    }

    public override bool IsTransientWriteFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: SqliteBusy or SqliteLocked } or
                SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey })
            {
                return true;
            }
        }

        return false;
    }

    // WAL lets readers run while a statement batch is written; the busy timeout makes a second writer wait for
    // the lock instead of failing at once. journal_mode is stored in the database file, busy_timeout is per
    // connection.
    private sealed class SqlitePragmas : DbConnectionInterceptor
    {
        public static readonly SqlitePragmas Instance = new();

        private const string Pragmas = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 30000;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Pragmas;
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = Pragmas;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

// Used by dotnet-ef when generating migrations (eng/ef-migrations.sh); no database is opened.
internal sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<XapiLrsDbContext>
{
    public XapiLrsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<XapiLrsDbContext>();
        new SqliteDatabaseProvider("Data Source=design-time.db").Configure(options);
        return new XapiLrsDbContext(options.Options);
    }
}
