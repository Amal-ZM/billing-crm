using BillingCrm.Application.Auth;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Auth;

public sealed class EfStaffUserStore(BillingDbContext context) : IStaffUserStore
{
    public Task<StaffUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.StaffUsers.AsNoTracking().FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task AddAsync(StaffUser user, CancellationToken cancellationToken)
    {
        context.StaffUsers.Add(user);
        await context.SaveChangesAsync(cancellationToken);
    }
}
