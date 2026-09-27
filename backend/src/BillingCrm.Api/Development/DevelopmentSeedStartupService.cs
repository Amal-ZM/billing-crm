using BillingCrm.Infrastructure;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Api.Development;

/// <summary>
/// Runs <see cref="DevelopmentSeeder"/> at start-up, only in the Development environment and only
/// against a LocalDB database, so the known dev credentials can never land on a shared server.
/// </summary>
public sealed class DevelopmentSeedStartupService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DevelopmentSeedStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        if (!TargetsLocalDb(configuration[DependencyInjection.ConnectionStringKey]))
        {
            // Nothing about the connection string is logged, only the decision.
            logger.LogWarning(
                "Development seeding skipped: the configured database is not a LocalDB instance. " +
                "The dev accounts are only ever created on a developer's own LocalDB.");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

            if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                logger.LogWarning(
                    "Development seeding skipped: database migrations are pending. Run 'dotnet ef database update' and restart the API.");
                return;
            }

            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(cancellationToken);
            logger.LogInformation("Development seed accounts are in place.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Convenience only: a database problem here must not stop the API. Log the type, never the
            // message, which can contain server details.
            logger.LogWarning(
                "Development seeding skipped: the database could not be used ({ExceptionType}). See the README setup steps.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>True only when the connection string's server is a LocalDB instance, e.g. <c>(localdb)\MSSQLLocalDB</c>.</summary>
    public static bool TargetsLocalDb(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
            return dataSource.StartsWith(@"(localdb)\", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
