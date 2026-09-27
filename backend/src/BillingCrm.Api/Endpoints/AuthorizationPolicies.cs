namespace BillingCrm.Api.Endpoints;

public static class AuthorizationPolicies
{
    /// <summary>Requires the Admin role. Agents are authenticated but refused (403).</summary>
    public const string AdminOnly = "AdminOnly";
}
