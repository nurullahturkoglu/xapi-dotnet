using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SQLitePCL;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Sqlite;

internal sealed class SqliteDatabaseProvider(string connectionString) : XapiLrsDatabaseProvider
{
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

    // SQLITE_BUSY and SQLITE_LOCKED are not retried. They only get here after the wait for the lock has run out
    // (the 30-second busy timeout, and Microsoft.Data.Sqlite's own retries until the command timeout), and each of
    // the store's retries would wait as long again: about 4 minutes before the request failed.
    public override bool IsTransientWriteFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey })
            {
                return true;
            }
        }

        return false;
    }

    // WAL lets readers run while a statement batch is written; the busy timeout makes a second writer wait for
    // the lock instead of failing at once. journal_mode is stored in the database file, busy_timeout is per
    // connection. A read-only connection cannot switch the journal mode of a database that is not in WAL mode yet,
    // so it only gets the busy timeout: EF Core checks that the database exists on one (Mode=ReadOnly), and the
    // health check would otherwise report a database without the migrations as unreachable.
    private sealed class SqlitePragmas : DbConnectionInterceptor
    {
        public static readonly SqlitePragmas Instance = new();

        private const string Pragmas = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 30000;";
        private const string ReadOnlyPragmas = "PRAGMA busy_timeout = 30000;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = PragmasFor(connection);
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = PragmasFor(connection);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // sqlite3_db_readonly also covers a file SQLite opened read-only because it is write-protected
        private static string PragmasFor(DbConnection connection) =>
            connection is SqliteConnection { Handle: { } handle } && raw.sqlite3_db_readonly(handle, "main") == 1
                ? ReadOnlyPragmas
                : Pragmas;
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
