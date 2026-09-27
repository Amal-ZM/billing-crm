using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests.Fakes;

/// <summary>In-memory store for service tests. Can be told to lose the duplicate-SKU race.</summary>
public sealed class FakeProductStore : IProductStore
{
    private readonly List<Product> _products = [];

    /// <summary>When true, <see cref="TryAddAsync"/> reports a duplicate even if the pre-check passed.</summary>
    public bool TryAddLosesRace { get; set; }

    /// <summary>When true, <see cref="TryUpdateAsync"/> reports a duplicate even if the pre-check passed.</summary>
    public bool TryUpdateLosesRace { get; set; }

    public int TryAddCalls { get; private set; }

    public int TryUpdateCalls { get; private set; }

    public ProductSearch? LastSearch { get; private set; }

    public IReadOnlyList<Product> All => _products;

    public void Seed(Product product) => _products.Add(product);

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_products.FirstOrDefault(product => product.Id == id));

    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(_products.Any(product => product.Sku == sku && product.Id != excludeId));

    public Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken)
    {
        TryAddCalls++;
        if (TryAddLosesRace || _products.Any(existing => existing.Sku == product.Sku))
        {
            return Task.FromResult(false);
        }

        _products.Add(product);
        return Task.FromResult(true);
    }

    public Task<bool> TryUpdateAsync(Product product, CancellationToken cancellationToken)
    {
        TryUpdateCalls++;
        var duplicate = _products.Any(existing => existing.Sku == product.Sku && existing.Id != product.Id);
        return Task.FromResult(!(TryUpdateLosesRace || duplicate));
    }

    public Task<PagedResult<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        var query = _products.AsEnumerable();

        query = search.Status switch
        {
            ProductStatusFilter.Active => query.Where(product => product.IsActive),
            ProductStatusFilter.Inactive => query.Where(product => !product.IsActive),
            _ => query,
        };

        var ordered = query.OrderBy(product => product.Name).ThenBy(product => product.Id).ToList();
        var offset = (int)Math.Min((long)(search.Page - 1) * search.PageSize, int.MaxValue);
        var page = ordered.Skip(offset).Take(search.PageSize).ToList();
        return Task.FromResult(PagedResult<Product>.Create(page, search.Page, search.PageSize, ordered.Count));
    }
}
