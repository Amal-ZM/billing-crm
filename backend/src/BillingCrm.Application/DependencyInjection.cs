using BillingCrm.Application.Auth;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace BillingCrm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<ProductService>();
        services.AddScoped<InvoiceService>();
        return services;
    }
}
