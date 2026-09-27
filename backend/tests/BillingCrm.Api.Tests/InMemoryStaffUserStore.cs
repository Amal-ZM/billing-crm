using System.Collections.Concurrent;
using BillingCrm.Application.Auth;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Auth;

namespace BillingCrm.Api.Tests;

/// <summary>Test-only credentials for the fake store. These are not the dev seed accounts and reach no real system.</summary>
public static class TestUsers
{
    public const string AdminEmail = "admin.test@example.test";
    public const string AdminPassword = "test-only-admin-password";
    public const string AgentEmail = "agent.test@example.test";
    public const string AgentPassword = "test-only-agent-password";
}

public sealed class InMemoryStaffUserStore : IStaffUserStore
{
    private readonly ConcurrentDictionary<string, StaffUser> _users = new();

    public static InMemoryStaffUserStore WithTestUsers()
    {
        var hasher = new IdentityPasswordHasher();
        var store = new InMemoryStaffUserStore();
        store.Add(StaffUser.Create(TestUsers.AdminEmail, hasher.Hash(TestUsers.AdminPassword), StaffRole.Admin));
        store.Add(StaffUser.Create(TestUsers.AgentEmail, hasher.Hash(TestUsers.AgentPassword), StaffRole.Agent));
        return store;
    }

    public IReadOnlyCollection<StaffUser> All => _users.Values.ToList();

    public void Add(StaffUser user) => _users[user.NormalizedEmail] = user;

    public Task<StaffUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(normalizedEmail));

    public Task AddAsync(StaffUser user, CancellationToken cancellationToken)
    {
        Add(user);
        return Task.CompletedTask;
    }
}
