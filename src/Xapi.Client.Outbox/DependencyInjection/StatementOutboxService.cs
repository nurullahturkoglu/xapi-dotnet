using Microsoft.Extensions.Hosting;
using Xapi.Client.Outbox;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Runs the registered <see cref="StatementOutbox"/>'s background loop for the lifetime of the host.</summary>
internal sealed class StatementOutboxService(StatementOutbox outbox) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => outbox.RunAsync(stoppingToken);
}
