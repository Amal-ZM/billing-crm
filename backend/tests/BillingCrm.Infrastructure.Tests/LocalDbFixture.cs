using BillingCrm.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// Gives a test class its own throwaway LocalDB database with the real migrations applied, and deletes
/// only that database afterwards. The application's own database is never touched.
/// The connection is built here from parts (Windows authentication, so there are no credentials) rather
/// than committed as a connection string.
/// </summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    private const string DatabaseNamePrefix = "BillingCrm_Tests_";
    private const string ServerOverrideVariable = "BILLINGCRM_TEST_SERVER";

    public string DatabaseName { get; } = DatabaseNamePrefix + Guid.NewGuid().ToString("N");

    private string ConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = Environment.GetEnvironmentVariable(ServerOverrideVariable) is { Length: > 0 } server
            ? server
            : @"(localdb)\MSSQLLocalDB",
        InitialCatalog = DatabaseName,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
        ConnectTimeout = 30,
    }.ConnectionString;

    public BillingDbContext CreateContext() => new(
        new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(BillingDbContext).Assembly.FullName))
            .Options);

    /// <summary>A raw, unopened connection to the same throwaway database, for a test that needs its own explicit transaction (spec
    /// 012's deadlock test: forcing a genuine SQL Server deadlock needs two real connections taking locks in opposite order).</summary>
    public SqlConnection CreateConnection() => new(ConnectionString);

    public async Task InitializeAsync()
    {
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            // Do not leave a half-built database behind if migrating failed part-way.
            await TryDeleteAsync();

            // Fail loudly rather than skipping: an untested search or unique index is not a pass.
            throw new InvalidOperationException(
                "These tests need SQL Server LocalDB (instance MSSQLLocalDB, or the server named in the " +
                $"{ServerOverrideVariable} environment variable). Check 'sqllocaldb i' and try again.",
                exception);
        }
    }

    public Task DisposeAsync() => TryDeleteAsync();

    /// <summary>Best effort: cleanup must never turn a passing run into a failure, or hide the real one.</summary>
    private async Task TryDeleteAsync()
    {
        try
        {
            await using var context = CreateContext();

            // The guard checks the database this context will really delete, not a value we just built.
            var target = context.Database.GetDbConnection().Database;
            if (!target.StartsWith(DatabaseNamePrefix, StringComparison.Ordinal))
            {
                return;
            }

            await context.Database.EnsureDeletedAsync();
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            // LocalDB unreachable or the database never existed: nothing to clean up.
        }
    }
}
