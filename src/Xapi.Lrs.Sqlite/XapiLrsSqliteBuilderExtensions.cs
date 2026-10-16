using Xapi.Lrs.Sqlite;
using Xapi.Lrs.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Stores the LRS data in SQLite.</summary>
public static class XapiLrsSqliteBuilderExtensions
{
    /// <summary>
    /// Stores the LRS data in a SQLite database. The database runs in WAL mode with a busy timeout, and
    /// statement writes take the write lock when they start (<c>BEGIN IMMEDIATE</c>), so concurrent writers
    /// queue instead of failing.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddXapiLrs</c>.</param>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string, such as <c>Data Source=lrs.db</c>.</param>
    /// <returns>The builder.</returns>
    public static IXapiLrsBuilder UseSqlite(this IXapiLrsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        return builder.UseDatabaseProvider(new SqliteDatabaseProvider(connectionString));
    }
}
