using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Products;

public sealed class EfProductStore(BillingDbContext context) : IProductStore
{
    // SQL Server error numbers for a violated unique index / unique constraint.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private const string SkuIndexName = "IX_Products_Sku";

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Products.AnyAsync(
            product => product.Sku == sku && (excludeId == null || product.Id != excludeId),
            cancellationToken);

    public async Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken)
    {
        context.Products.Add(product);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateSku(exception))
        {
            // The unique index refused it (for example two saves at the same instant). Forget the
            // rejected row so this context stays usable, and report the expected outcome.
            context.Entry(product).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<PagedResult<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken)
    {
        var query = context.Products.AsNoTracking();

        query = search.Status switch
        {
            ProductStatusFilter.Active => query.Where(product => product.IsActive),
            ProductStatusFilter.Inactive => query.Where(product => !product.IsActive),
            _ => query,
        };

        if (search.Unit is { } unit)
        {
            query = query.Where(product => product.Unit == unit);
        }

        if (search.Text?.Trim() is { Length: > 0 } text)
        {
            // Contains-match on name or SKU. SQL Server's default collation ignores letter case; % _ [ and the
            // escape character in the person's text are escaped so they match themselves, not as wildcards.
            var pattern = $"%{EscapeLike(text)}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name, pattern, LikeEscape)
                || EF.Functions.Like(product.Sku, pattern, LikeEscape));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Long arithmetic: a huge page number must not wrap around into a negative or arbitrary offset.
        // A page past the last one is an empty page with the real total, without asking the database.
        var offset = (long)(search.Page - 1) * search.PageSize;
        var items = offset >= totalCount
            ? []
            : await query
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id)
                .Skip((int)offset)
                .Take(search.PageSize)
                .ToListAsync(cancellationToken);

        return PagedResult<Product>.Create(items, search.Page, search.PageSize, totalCount);
    }

    public async Task<bool> TryUpdateAsync(Product product, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateSku(exception))
        {
            // The unique index refused the new SKU (someone else took it a moment ago). Put the tracked
            // product back to what is stored so this context does not go on believing the edit happened.
            await context.Entry(product).ReloadAsync(cancellationToken);
            return false;
        }
    }

    private const string LikeEscape = "\\";

    private static string EscapeLike(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal);

    /// <summary>Only a violation of the SKU index counts, so any other unique failure is not mistaken for "SKU in use".</summary>
    private static bool IsDuplicateSku(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(SkuIndexName, StringComparison.OrdinalIgnoreCase);
}
