using BillingCrm.Application.Auth;
using BillingCrm.Domain;

namespace BillingCrm.Api.Development;

/// <summary>
/// DEVELOPMENT ONLY. Creates the two known local-development accounts if they are missing.
/// The passwords below are the non-real, documented dev values from CLAUDE.md; they must never be
/// used outside a developer's own LocalDB. Only hashes are stored. Existing accounts are never
/// changed. <see cref="DevelopmentSeedStartupService"/> only runs this in the Development environment.
/// </summary>
public sealed class DevelopmentSeeder(IStaffUserStore store, IPasswordHasher hasher)
{
    public const string AdminEmail = "admin@crm.local";
    public const string AdminPassword = "Admin#123";
    public const string AgentEmail = "agent1@crm.local";
    public const string AgentPassword = "Agent#123";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await EnsureAsync(AdminEmail, AdminPassword, StaffRole.Admin, cancellationToken);
        await EnsureAsync(AgentEmail, AgentPassword, StaffRole.Agent, cancellationToken);
    }

    private async Task EnsureAsync(string email, string password, StaffRole role, CancellationToken cancellationToken)
    {
        var existing = await store.FindByNormalizedEmailAsync(StaffUser.NormalizeEmail(email), cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await store.AddAsync(StaffUser.Create(email, hasher.Hash(password), role), cancellationToken);
    }
}
