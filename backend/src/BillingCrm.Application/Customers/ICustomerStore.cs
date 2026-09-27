using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Customers;

public interface ICustomerStore
{
    Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="normalizedEmail">Already normalised with <see cref="EmailNormalizer.Normalize"/>.</param>
    /// <param name="excludeId">A customer to ignore, so saving a customer with its own email is not a duplicate.</param>
    Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new customer. Returns false, without throwing, only when another customer already has the
    /// same normalized email (the database's unique index is the real guarantee); other failures throw.
    /// </summary>
    Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken);

    /// <summary>Saves changes to a customer returned by <see cref="FindByIdAsync"/>. Same false-on-duplicate rule.</summary>
    Task<bool> TryUpdateAsync(Customer customer, CancellationToken cancellationToken);

    Task<PagedResult<Customer>> SearchAsync(CustomerSearch search, CancellationToken cancellationToken);
}
