namespace Xapi.Lrs.Tests.Infrastructure;

public static class TestDatabases
{
    public const string PostgreSqlSwitch = "XAPI_POSTGRES_TESTS";

    // PostgreSQL runs in a Docker container (Testcontainers). The Linux CI runners have Docker; the macOS and
    // Windows runners cannot run Linux containers. So the PostgreSQL rows run on Linux by default, and
    // XAPI_POSTGRES_TESTS=1 or =0 turns them on or off anywhere (for example on a Mac with Docker Desktop).
    public static bool PostgreSqlEnabled { get; } =
        ReadSwitch(Environment.GetEnvironmentVariable(PostgreSqlSwitch)) ?? OperatingSystem.IsLinux();

    public static IEnumerable<TheoryDataRow<TestDatabase>> All()
    {
        yield return new(TestDatabase.Sqlite);
        yield return new(TestDatabase.PostgreSql)
        {
            Skip = PostgreSqlEnabled ? null : $"PostgreSQL tests need Docker: they run on Linux, or when {PostgreSqlSwitch}=1",
        };
    }

    private static bool? ReadSwitch(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "1" or "TRUE" => true,
        "0" or "FALSE" => false,
        _ => null,
    };
}
