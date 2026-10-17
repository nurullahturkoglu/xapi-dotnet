using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Xapi.Benchmarks.Lrs;

// Counts the statement store's "hit a concurrent write; retrying" log events. The store logs nothing else at
// Debug, and LrsServer lets no other category through below Warning.
public sealed class RetryCounter : ILoggerProvider, ILogger
{
    public const string Category = "Xapi.Lrs.Storage.EfStatementStore";

    private long _retries;

    public long Retries => Interlocked.Read(ref _retries);

    public ILogger CreateLogger(string categoryName) => categoryName == Category ? this : NullLogger.Instance;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Debug && exception is not null)
        {
            Interlocked.Increment(ref _retries);
        }
    }

    public void Dispose()
    {
    }
}
