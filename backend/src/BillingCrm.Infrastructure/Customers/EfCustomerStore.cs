using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Customers;

public sealed class EfCustomerStore(BillingDbContext context) : ICustomerStore
{
    // SQL Server error numbers for a violated unique index / unique constraint.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Customers.FirstOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Customers.AnyAsync(
            customer => customer.NormalizedEmail == normalizedEmail && (excludeId == null || customer.Id != excludeId),
            cancellationToken);

    public async Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken)
    {
        context.Customers.Add(customer);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            // The unique index refused it (for example two saves at the same instant). Forget the
            // rejected row so this context stays usable, and report the expected outcome.
            context.Entry(customer).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<PagedResult<Customer>> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        var query = context.Customers.AsNoTracking();

        if (search.IsActive is { } isActive)
        {
            query = query.Where(customer => customer.IsActive == isActive);
        }

        if (search.Type is { } type)
        {
            query = query.Where(customer => customer.Type == type);
        }

        if (search.Text?.Trim() is { Length: > 0 } text)
        {
            // Contains-match on four fields. SQL Server's default collation ignores letter case; % _ [ and the
            // escape character in the person's text are escaped so they match themselves, not as wildcards.
            var pattern = $"%{EscapeLike(text)}%";
            query = query.Where(customer =>
                EF.Functions.Like(customer.Name, pattern, LikeEscape)
                || EF.Functions.Like(customer.Email, pattern, LikeEscape)
                || EF.Functions.Like(customer.Phone, pattern, LikeEscape)
                || (customer.VatNumber != null && EF.Functions.Like(customer.VatNumber, pattern, LikeEscape)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Long arithmetic: a huge page number must not wrap around into a negative or arbitrary offset.
        // A page past the last one is an empty page with the real total, without asking the database.
        var offset = (long)(search.Page - 1) * search.PageSize;
        var items = offset >= totalCount
            ? []
            : await query
                .OrderBy(customer => customer.Name)
                .ThenBy(customer => customer.Id)
                .Skip((int)offset)
                .Take(search.PageSize)
                .ToListAsync(cancellationToken);

        return PagedResult<Customer>.Create(items, search.Page, search.PageSize, totalCount);
    }

    public async Task<bool> TryUpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            // The unique index refused the new email (someone else took it a moment ago). Put the tracked
            // customer back to what is stored so this context does not go on believing the edit happened.
            await context.Entry(customer).ReloadAsync(cancellationToken);
            return false;
        }
    }

    private const string LikeEscape = "\\";

    private static string EscapeLike(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);

    /// <summary>Only a violation of the email index counts, so any other unique failure is not mistaken for "email in use".</summary>
    private static bool IsDuplicateEmail(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(EmailIndexName, StringComparison.OrdinalIgnoreCase);

    private const string EmailIndexName = "IX_Customers_NormalizedEmail";
}
