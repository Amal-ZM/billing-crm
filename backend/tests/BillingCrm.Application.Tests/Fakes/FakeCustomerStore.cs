using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests.Fakes;

/// <summary>In-memory store for service tests. Can be told to lose the duplicate-email race.</summary>
public sealed class FakeCustomerStore : ICustomerStore
{
    private readonly List<Customer> _customers = [];

    /// <summary>When true, <see cref="TryAddAsync"/> reports a duplicate even if the pre-check passed.</summary>
    public bool TryAddLosesRace { get; set; }

    /// <summary>When true, <see cref="TryUpdateAsync"/> reports a duplicate even if the pre-check passed.</summary>
    public bool TryUpdateLosesRace { get; set; }

    public int TryAddCalls { get; private set; }

    public int TryUpdateCalls { get; private set; }

    public CustomerSearch? LastSearch { get; private set; }

    public IReadOnlyList<Customer> All => _customers;

    public void Seed(Customer customer) => _customers.Add(customer);

    public Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.FirstOrDefault(customer => customer.Id == id));

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.Any(customer => customer.NormalizedEmail == normalizedEmail && customer.Id != excludeId));

    public Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken)
    {
        TryAddCalls++;
        if (TryAddLosesRace || _customers.Any(existing => existing.NormalizedEmail == customer.NormalizedEmail))
        {
            return Task.FromResult(false);
        }

        _customers.Add(customer);
        return Task.FromResult(true);
    }

    public Task<bool> TryUpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        TryUpdateCalls++;
        var duplicate = _customers.Any(existing => existing.NormalizedEmail == customer.NormalizedEmail && existing.Id != customer.Id);
        return Task.FromResult(!(TryUpdateLosesRace || duplicate));
    }

    public Task<PagedResult<Customer>> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        var ordered = _customers.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id).ToList();
        var offset = (int)Math.Min((long)(search.Page - 1) * search.PageSize, int.MaxValue);
        var page = ordered.Skip(offset).Take(search.PageSize).ToList();
        return Task.FromResult(PagedResult<Customer>.Create(page, search.Page, search.PageSize, ordered.Count));
    }
}
