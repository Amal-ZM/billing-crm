using BillingCrm.Domain;

namespace BillingCrm.Application.Auth;

public interface IStaffUserStore
{
    /// <param name="normalizedEmail">Already normalised with <see cref="StaffUser.NormalizeEmail"/>.</param>
    Task<StaffUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task AddAsync(StaffUser user, CancellationToken cancellationToken);
}
