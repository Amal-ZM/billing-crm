using BillingCrm.Application.Auth;
using BillingCrm.Domain;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>Stands in for the signed-in user when a test builds an <c>InvoiceService</c> by hand (spec 010).</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public Actor Current { get; set; } = new("agent1@crm.local", StaffRole.Agent);
}
