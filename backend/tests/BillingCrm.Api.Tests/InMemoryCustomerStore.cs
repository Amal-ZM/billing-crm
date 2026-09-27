using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>
/// In-memory store for HTTP-level tests. Its search approximates the real one (contains, ignoring case);
/// the real semantics (LIKE, escaping, Arabic, collation) are proven in the Infrastructure tests.
/// </summary>
public sealed class InMemoryCustomerStore : ICustomerStore
{
    private readonly object _gate = new();
    private readonly List<Customer> _customers = [];

    public IReadOnlyList<Customer> All
    {
        get
        {
            lock (_gate)
            {
                return _customers.ToList();
            }
        }
    }

    public void Seed(Customer customer)
    {
        lock (_gate)
        {
            _customers.Add(customer);
        }
    }

    public Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_customers.FirstOrDefault(customer => customer.Id == id));
        }
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_customers.Any(customer => customer.NormalizedEmail == normalizedEmail && customer.Id != excludeId));
        }
    }

    public Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_customers.Any(existing => existing.NormalizedEmail == customer.NormalizedEmail))
            {
                return Task.FromResult(false);
            }

            _customers.Add(customer);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var duplicate = _customers.Any(existing => existing.NormalizedEmail == customer.NormalizedEmail && existing.Id != customer.Id);
            return Task.FromResult(!duplicate);
        }
    }

    public Task<PagedResult<Customer>> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _customers.AsEnumerable();

            if (search.IsActive is { } isActive)
            {
                query = query.Where(customer => customer.IsActive == isActive);
            }

            if (search.Type is { } type)
            {
                query = query.Where(customer => customer.Type == type);
            }

            if (search.Text is { } text)
            {
                bool Has(string? value) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
                query = query.Where(customer => Has(customer.Name) || Has(customer.Email) || Has(customer.Phone) || Has(customer.VatNumber));
            }

            var ordered = query.OrderBy(customer => customer.Name, StringComparer.Ordinal).ThenBy(customer => customer.Id).ToList();
            var offset = (int)Math.Min((long)(search.Page - 1) * search.PageSize, int.MaxValue);
            var page = ordered.Skip(offset).Take(search.PageSize).ToList();
            return Task.FromResult(PagedResult<Customer>.Create(page, search.Page, search.PageSize, ordered.Count));
        }
    }
}
