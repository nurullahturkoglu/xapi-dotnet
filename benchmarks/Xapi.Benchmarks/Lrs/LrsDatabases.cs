namespace Xapi.Benchmarks.Lrs;

// SQLite always runs. PostgreSQL runs when XAPI_BENCH_POSTGRES=1 (Program starts a container with Testcontainers
// and passes its connection string down) or when XAPI_BENCH_POSTGRES_CONNECTION points at a server of your own.
public static class LrsDatabases
{
    public const string Sqlite = "SQLite";
    public const string PostgreSql = "PostgreSQL";

    public const string PostgreSqlSwitch = "XAPI_BENCH_POSTGRES";
    public const string PostgreSqlConnectionVariable = "XAPI_BENCH_POSTGRES_CONNECTION";

    public static string PostgreSqlConnectionString =>
        Environment.GetEnvironmentVariable(PostgreSqlConnectionVariable) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException($"Set {PostgreSqlSwitch}=1 or {PostgreSqlConnectionVariable} to run the PostgreSQL benchmarks.");

    // The values of the Database parameter of the LRS benchmarks
    public static IEnumerable<string> All()
    {
        yield return Sqlite;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PostgreSqlConnectionVariable)))
        {
            yield return PostgreSql;
        }
    }
}
