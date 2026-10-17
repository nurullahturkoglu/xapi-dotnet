using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds the health check of the xAPI LRS.</summary>
public static class XapiLrsHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check that reports whether the database of the LRS added with <c>AddXapiLrs</c> can be
    /// reached and has every migration of the provider package applied.
    /// </summary>
    /// <remarks>
    /// A database that cannot be reached, and one with migrations that are not applied, report
    /// <paramref name="failureStatus"/>: in both cases requests to the LRS fail. Map the check with
    /// <c>MapHealthChecks</c>, usually on a path a readiness probe uses. Each run opens a connection and reads the
    /// migrations history table.
    /// </remarks>
    /// <param name="builder">The builder returned by <c>AddHealthChecks</c>.</param>
    /// <param name="name">The name of the check in the health report.</param>
    /// <param name="failureStatus">
    /// The status to report when the check fails, or <see langword="null"/> for <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Tags to filter health checks by, for example <c>ready</c>.</param>
    /// <param name="timeout">How long the check may take, or <see langword="null"/> for no timeout of its own.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IHealthChecksBuilder AddXapiLrsHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "xapi-lrs",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return builder.Add(new HealthCheckRegistration(
            name,
            static provider => new XapiLrsHealthCheck(provider.GetService<IDbContextFactory<XapiLrsDbContext>>()),
            failureStatus,
            tags,
            timeout));
    }
}
