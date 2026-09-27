using BillingCrm.Api.Development;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BillingCrm.Api.Tests;

public class DevelopmentSeederTests
{
    private const string LocalDbConnection = @"Server=(localdb)\test-only;Database=Placeholder;Trusted_Connection=True";

    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public async Task Seeds_exactly_the_two_dev_accounts_with_hashed_passwords()
    {
        var store = new InMemoryStaffUserStore();

        await new DevelopmentSeeder(store, _hasher).SeedAsync(CancellationToken.None);

        var admin = await store.FindByNormalizedEmailAsync(StaffUser.NormalizeEmail(DevelopmentSeeder.AdminEmail), default);
        var agent = await store.FindByNormalizedEmailAsync(StaffUser.NormalizeEmail(DevelopmentSeeder.AgentEmail), default);

        Assert.Equal(2, store.All.Count);
        Assert.Equal(StaffRole.Admin, admin!.Role);
        Assert.Equal(StaffRole.Agent, agent!.Role);
        Assert.NotEqual(DevelopmentSeeder.AdminPassword, admin.PasswordHash);
        Assert.NotEqual(DevelopmentSeeder.AgentPassword, agent.PasswordHash);
        Assert.True(_hasher.Verify(admin.PasswordHash, DevelopmentSeeder.AdminPassword));
        Assert.True(_hasher.Verify(agent.PasswordHash, DevelopmentSeeder.AgentPassword));
    }

    [Fact]
    public async Task Running_twice_creates_no_duplicates_and_never_changes_an_existing_account()
    {
        var store = new InMemoryStaffUserStore();
        var existing = StaffUser.Create(DevelopmentSeeder.AdminEmail, _hasher.Hash("a-changed-password"), StaffRole.Admin);
        store.Add(existing);
        var seeder = new DevelopmentSeeder(store, _hasher);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(2, store.All.Count);
        var admin = await store.FindByNormalizedEmailAsync(StaffUser.NormalizeEmail(DevelopmentSeeder.AdminEmail), default);
        Assert.Equal(existing.PasswordHash, admin!.PasswordHash);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Startup_service_does_nothing_outside_development(string environmentName)
    {
        var scopeFactory = new ThrowingScopeFactory();
        var service = CreateService(scopeFactory, environmentName, LocalDbConnection);

        await service.StartAsync(CancellationToken.None);

        Assert.False(scopeFactory.WasUsed);
    }

    [Theory]
    [InlineData(@"Server=prod-sql.example.test;Database=BillingCrm;Trusted_Connection=True")]
    [InlineData(@"Server=tcp:shared.example.test,1433;Database=BillingCrm;User Id=someone")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Startup_service_refuses_to_seed_a_database_that_is_not_localdb_even_in_development(string? connectionString)
    {
        var scopeFactory = new ThrowingScopeFactory();
        var service = CreateService(scopeFactory, "Development", connectionString);

        await service.StartAsync(CancellationToken.None);

        Assert.False(scopeFactory.WasUsed);
    }

    [Theory]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Database=X;Trusted_Connection=True", true)]
    [InlineData(@"Data Source=(LocalDb)\v11.0;Initial Catalog=X", true)]
    [InlineData(@"Server=localhost;Database=X;Trusted_Connection=True", false)]
    [InlineData(@"Server=(localdb).evil.example;Database=X", false)]
    [InlineData("not a connection string", false)]
    public void TargetsLocalDb_reads_the_server_from_the_connection_string(string connectionString, bool expected)
    {
        Assert.Equal(expected, DevelopmentSeedStartupService.TargetsLocalDb(connectionString));
    }

    private static DevelopmentSeedStartupService CreateService(IServiceScopeFactory scopeFactory, string environmentName, string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
            .Build();

        return new DevelopmentSeedStartupService(
            scopeFactory,
            new FakeEnvironment(environmentName),
            configuration,
            NullLogger<DevelopmentSeedStartupService>.Instance);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public bool WasUsed { get; private set; }

        public IServiceScope CreateScope()
        {
            WasUsed = true;
            throw new InvalidOperationException("The seeder must not touch services here.");
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "BillingCrm.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
