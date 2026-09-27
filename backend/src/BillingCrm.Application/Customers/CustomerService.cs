using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Customers;

public sealed class CustomerService(ICustomerStore store, TimeProvider time)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Longer text cannot match anything useful (the longest searched field is 256) and only costs a scan.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>The one failure for a duplicate email, whether the pre-check or the database's unique index caught it.</summary>
    public static readonly Error EmailInUse = Error.Conflict(
        "email_in_use",
        "Email already in use",
        "Another customer already uses this email address.");

    public static readonly Error NotFound = Error.NotFound(
        "customer_not_found",
        "Customer not found",
        "No customer exists with this id.");

    public async Task<Result<CustomerDto>> CreateAsync(CustomerInput input, CancellationToken cancellationToken)
    {
        var validated = CustomerValidator.Validate(input);
        if (validated.IsFailure)
        {
            return Result.Fail<CustomerDto>(validated.Error!);
        }

        var details = validated.Value;

        // Friendly pre-check; the unique index below is what actually guarantees uniqueness.
        if (await store.EmailExistsAsync(EmailNormalizer.Normalize(details.Email), null, cancellationToken))
        {
            return Result.Fail<CustomerDto>(EmailInUse);
        }

        var customer = Customer.Create(
            details.Name,
            details.Type,
            details.Email,
            details.Phone,
            details.VatNumber,
            details.Address,
            time.GetUtcNow().UtcDateTime);

        return await store.TryAddAsync(customer, cancellationToken)
            ? Result.Success(CustomerDto.From(customer))
            : Result.Fail<CustomerDto>(EmailInUse);
    }

    public async Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await store.FindByIdAsync(id, cancellationToken);
        return customer is null
            ? Result.Fail<CustomerDto>(NotFound)
            : Result.Success(CustomerDto.From(customer));
    }

    public async Task<Result<CustomerDto>> UpdateAsync(Guid id, CustomerInput input, CancellationToken cancellationToken)
    {
        var customer = await store.FindByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return Result.Fail<CustomerDto>(NotFound);
        }

        var validated = CustomerValidator.Validate(input);
        if (validated.IsFailure)
        {
            return Result.Fail<CustomerDto>(validated.Error!);
        }

        var details = validated.Value;

        // The customer's own current email is not a duplicate of itself.
        if (await store.EmailExistsAsync(EmailNormalizer.Normalize(details.Email), customer.Id, cancellationToken))
        {
            return Result.Fail<CustomerDto>(EmailInUse);
        }

        var changed = customer.Update(
            details.Name,
            details.Type,
            details.Email,
            details.Phone,
            details.VatNumber,
            details.Address,
            time.GetUtcNow().UtcDateTime);

        if (!changed)
        {
            return Result.Success(CustomerDto.From(customer));
        }

        return await store.TryUpdateAsync(customer, cancellationToken)
            ? Result.Success(CustomerDto.From(customer))
            : Result.Fail<CustomerDto>(EmailInUse);
    }

    /// <summary>Retires or restores a customer. Idempotent: the state it already has changes nothing.</summary>
    public async Task<Result<CustomerDto>> SetActiveAsync(Guid id, SetActiveRequest request, CancellationToken cancellationToken)
    {
        var customer = await store.FindByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return Result.Fail<CustomerDto>(NotFound);
        }

        if (request.IsActive is not { } isActive)
        {
            return Result.Fail<CustomerDto>(Error.Validation(new Dictionary<string, string[]>
            {
                ["isActive"] = ["IsActive is required and must be true or false."],
            }));
        }

        if (customer.SetActive(isActive, time.GetUtcNow().UtcDateTime)
            && !await store.TryUpdateAsync(customer, cancellationToken))
        {
            // Only the email is unique, and this does not touch it: reaching here is a genuine bug.
            throw new InvalidOperationException("Changing the active state cannot violate the unique email index.");
        }

        return Result.Success(CustomerDto.From(customer));
    }

    public async Task<Result<PagedResult<CustomerListItem>>> SearchAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var page = 1;
        if (!string.IsNullOrWhiteSpace(query.Page)
            && (!int.TryParse(query.Page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = ["Page must be a whole number of 1 or more."];
        }

        var pageSize = DefaultPageSize;
        if (!string.IsNullOrWhiteSpace(query.PageSize)
            && (!int.TryParse(query.PageSize, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pageSize)
                || pageSize is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Page size must be a whole number from 1 to {MaxPageSize}."];
        }

        var text = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        if (text is { Length: > MaxSearchLength })
        {
            errors["q"] = [$"Search text must be at most {MaxSearchLength} characters."];
        }

        CustomerType? type = null;
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (CustomerValidator.TryParseType(query.Type, out var parsedType))
            {
                type = parsedType;
            }
            else
            {
                errors["type"] = ["Type must be Company or Individual."];
            }
        }

        // Only "true" and "false" (any case); "1", "yes" and the like are refused rather than guessed at.
        bool? isActive = null;
        if (!string.IsNullOrWhiteSpace(query.Active))
        {
            if (bool.TryParse(query.Active, out var parsedActive))
            {
                isActive = parsedActive;
            }
            else
            {
                errors["active"] = ["Active must be true or false."];
            }
        }

        if (errors.Count > 0)
        {
            return Result.Fail<PagedResult<CustomerListItem>>(Error.Validation(errors));
        }

        var found = await store.SearchAsync(new CustomerSearch(text, type, isActive, page, pageSize), cancellationToken);
        return Result.Success(found.Map(CustomerListItem.From));
    }
}
