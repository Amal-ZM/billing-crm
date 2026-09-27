using System.Net;
using BillingCrm.Application.Auth;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Products;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Boots the API in-process. Uses test-only placeholder settings (never contacted), swaps the real
/// database health check for a fake, and replaces the user store with an in-memory one so tests
/// never need LocalDB.
/// </summary>
public class ApiFactory(
    HealthStatus databaseHealth = HealthStatus.Healthy,
    string? connectionString = ApiFactory.PlaceholderConnectionString,
    string? corsOriginOverride = null,
    string? signingKey = ApiFactory.TestSigningKey) : WebApplicationFactory<Program>
{
    // Placeholder for tests only; there is no server behind it.
    public const string PlaceholderConnectionString = "Server=test-only-placeholder;Database=Placeholder;Trusted_Connection=True";

    // Obviously fake signing key for tests only (at least 32 bytes); it signs nothing outside these tests.
    public const string TestSigningKey = "test-only-signing-key-not-a-secret-0123456789";

    /// <summary>The fake user store the API uses in tests; seeded with one Admin and one Agent.</summary>
    public InMemoryStaffUserStore Store { get; } = InMemoryStaffUserStore.WithTestUsers();

    /// <summary>The fake customer store the API uses in tests; starts empty.</summary>
    public InMemoryCustomerStore CustomerStore { get; } = new();

    /// <summary>The fake product store the API uses in tests; starts empty.</summary>
    public InMemoryProductStore ProductStore { get; } = new();

    /// <summary>The fake invoice store the API uses in tests; starts empty and hands out INV numbers from 1.</summary>
    public InMemoryInvoiceStore InvoiceStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps user-secrets (Development only) from leaking real values into tests.
        builder.UseEnvironment("Testing");

        // A null value means "not supplied": blank the key explicitly so a machine that exports the
        // matching environment variable cannot leak a real value into the test.
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString ?? string.Empty);
        builder.UseSetting("Jwt:SigningKey", signingKey ?? string.Empty);

        if (corsOriginOverride is not null)
        {
            // Replaces the single default origin (index 0) from appsettings.json.
            builder.UseSetting("Cors:AllowedOrigins:0", corsOriginOverride);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "database",
                    new FakeDatabaseCheck(databaseHealth),
                    failureStatus: null,
                    tags: null));
            });

            services.RemoveAll<IStaffUserStore>();
            services.AddSingleton<IStaffUserStore>(Store);

            services.RemoveAll<ICustomerStore>();
            services.AddSingleton<ICustomerStore>(CustomerStore);

            services.RemoveAll<IProductStore>();
            services.AddSingleton<IProductStore>(ProductStore);

            // The list joins customer names, as the real store does; it reads them from this factory's customer store.
            InvoiceStore.Customers = CustomerStore;
            services.RemoveAll<IInvoiceStore>();
            services.AddSingleton<IInvoiceStore>(InvoiceStore);

            // TestServer never simulates a real socket, so Connection.RemoteIpAddress is otherwise always null (every
            // test request would land in the login rate limiter's own "same source" partition). This lets a test that
            // needs to look like a different source say so, without changing production code or its own partition
            // key (still Connection.RemoteIpAddress): send the test-only header TestRemoteIpHeader.
            services.RemoveAll<IHttpContextFactory>();
            services.AddSingleton<IHttpContextFactory>(sp => new TestRemoteIpHttpContextFactory(new DefaultHttpContextFactory(sp)));
        });
    }

    /// <summary>Test-only request header name: its value, when present, becomes <c>HttpContext.Connection.RemoteIpAddress</c>.</summary>
    public const string TestRemoteIpHeader = "X-Test-Remote-Ip";

    private sealed class FakeDatabaseCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(status));
    }

    private sealed class TestRemoteIpHttpContextFactory(IHttpContextFactory inner) : IHttpContextFactory
    {
        public HttpContext Create(IFeatureCollection featureCollection)
        {
            var context = inner.Create(featureCollection);
            if (context.Request.Headers.TryGetValue(TestRemoteIpHeader, out var ip) && IPAddress.TryParse(ip.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return context;
        }

        public void Dispose(HttpContext httpContext) => inner.Dispose(httpContext);
    }
}
