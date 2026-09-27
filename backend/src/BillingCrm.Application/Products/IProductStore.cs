using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Products;

public interface IProductStore
{
    Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="sku">Already normalized (trimmed, upper-case), as stored.</param>
    /// <param name="excludeId">A product to ignore, so saving a product with its own SKU is not a duplicate.</param>
    Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new product. Returns false, without throwing, only when another product already has the same
    /// SKU (the database's unique index is the real guarantee); other failures throw.
    /// </summary>
    Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>Saves changes to a product returned by <see cref="FindByIdAsync"/>. Same false-on-duplicate rule.</summary>
    Task<bool> TryUpdateAsync(Product product, CancellationToken cancellationToken);

    Task<PagedResult<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken);
}
