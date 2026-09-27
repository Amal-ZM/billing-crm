using BillingCrm.Application.Common;
using BillingCrm.Application.Products;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// In-memory store for HTTP-level tests. Its search approximates the real one (contains, ignoring case);
/// the real semantics (LIKE, escaping, Arabic, collation) are proven in the Infrastructure tests.
/// </summary>
public sealed class InMemoryProductStore : IProductStore
{
    private readonly object _gate = new();
    private readonly List<Product> _products = [];

    public IReadOnlyList<Product> All
    {
        get
        {
            lock (_gate)
            {
                return _products.ToList();
            }
        }
    }

    public void Seed(Product product)
    {
        lock (_gate)
        {
            _products.Add(product);
        }
    }

    public Task<Product?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_products.FirstOrDefault(product => product.Id == id));
        }
    }

    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_products.Any(product => product.Sku == sku && product.Id != excludeId));
        }
    }

    public Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_products.Any(existing => existing.Sku == product.Sku))
            {
                return Task.FromResult(false);
            }

            _products.Add(product);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateAsync(Product product, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var duplicate = _products.Any(existing => existing.Sku == product.Sku && existing.Id != product.Id);
            return Task.FromResult(!duplicate);
        }
    }

    public Task<PagedResult<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _products.AsEnumerable();

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

            if (search.Text is { } text)
            {
                query = query.Where(product =>
                    product.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || product.Sku.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query.OrderBy(product => product.Name, StringComparer.Ordinal).ThenBy(product => product.Id).ToList();
            var offset = (int)Math.Min((long)(search.Page - 1) * search.PageSize, int.MaxValue);
            var page = ordered.Skip(offset).Take(search.PageSize).ToList();
            return Task.FromResult(PagedResult<Product>.Create(page, search.Page, search.PageSize, ordered.Count));
        }
    }
}
