using BillingCrm.Application.Auth;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests.Fakes;

/// <summary>Stands in for the signed-in user (spec 010): set <see cref="Actor"/> to act as someone else, or <see cref="Missing"/> for a request with no usable user.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    private Actor _actor = new("agent1@crm.local", StaffRole.Agent);

    /// <summary>When true, reading <see cref="Current"/> throws, as the real adapter does for a request without a usable user (a defect).</summary>
    public bool Missing { get; set; }

    public Actor Current
    {
        get => Missing ? throw new InvalidOperationException("There is no signed-in user for this request.") : _actor;
        set => _actor = value;
    }
}
