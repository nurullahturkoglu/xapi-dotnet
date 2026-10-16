using Xapi.Lrs.PostgreSql;
using Xapi.Lrs.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Stores the LRS data in PostgreSQL.</summary>
public static class XapiLrsPostgreSqlBuilderExtensions
{
    /// <summary>
    /// Stores the LRS data in a PostgreSQL database. Statement writes run in serializable transactions and are
    /// retried when a concurrent write conflicts.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddXapiLrs</c>.</param>
    /// <param name="connectionString">An Npgsql connection string, such as <c>Host=localhost;Database=lrs;Username=lrs;Password=...</c>.</param>
    /// <returns>The builder.</returns>
    public static IXapiLrsBuilder UsePostgreSql(this IXapiLrsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        return builder.UseDatabaseProvider(new PostgreSqlDatabaseProvider(connectionString));
    }
}
