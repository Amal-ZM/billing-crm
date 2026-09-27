using BillingCrm.Application.Auth;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Products;
using BillingCrm.Infrastructure.Customers;
using BillingCrm.Infrastructure.Invoices;
using BillingCrm.Infrastructure.Products;
using BillingCrm.Infrastructure.Auth;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillingCrm.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseHealthCheckName = "database";
    public const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // A missing setting at start-up is a misconfiguration, not a business outcome: fail fast.
            // The message names the key only; it never echoes any value.
            throw new InvalidOperationException(
                $"Required setting '{ConnectionStringKey}' is missing. " +
                "Supply it via user-secrets or the ConnectionStrings__DefaultConnection environment variable; " +
                "see the README section 'Configure the database connection'.");
        }

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.Validate();
        services.AddSingleton(jwtOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<IStaffUserStore, EfStaffUserStore>();
        services.AddScoped<ICustomerStore, EfCustomerStore>();
        services.AddScoped<IProductStore, EfProductStore>();
        services.AddScoped<IInvoiceStore, EfInvoiceStore>();

        services.AddDbContext<BillingDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(BillingDbContext).Assembly.FullName)));

        services.AddHealthChecks()
            .AddDbContextCheck<BillingDbContext>(DatabaseHealthCheckName);

        return services;
    }
}
