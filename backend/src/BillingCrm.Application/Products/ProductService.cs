using System.Globalization;
using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Products;

public sealed class ProductService(IProductStore store, TimeProvider time)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Longer text cannot match anything useful (the longest searched field is 200) and only costs a scan.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>The one failure for a duplicate SKU, whether the pre-check or the database's unique index caught it.</summary>
    public static readonly Error SkuInUse = Error.Conflict(
        "sku_in_use",
        "SKU already in use",
        "Another product already uses this SKU.");

    public static readonly Error NotFound = Error.NotFound(
        "product_not_found",
        "Product not found",
        "No product exists with this id.");

    public async Task<Result<ProductDto>> CreateAsync(ProductInput input, CancellationToken cancellationToken)
    {
        var validated = ProductValidator.Validate(input, defaultVat: true);
        if (validated.IsFailure)
        {
            return Result.Fail<ProductDto>(validated.Error!);
        }

        var details = validated.Value;

        // Friendly pre-check; the unique index below is what actually guarantees uniqueness.
        if (await store.SkuExistsAsync(details.Sku, null, cancellationToken))
        {
            return Result.Fail<ProductDto>(SkuInUse);
        }

        var product = Product.Create(
            details.Name,
            details.Sku,
            details.Unit,
            details.UnitPrice,
            details.VatPercent,
            time.GetUtcNow().UtcDateTime);

        return await store.TryAddAsync(product, cancellationToken)
            ? Result.Success(ProductDto.From(product))
            : Result.Fail<ProductDto>(SkuInUse);
    }

    /// <summary>Edits a product. Admin only (the endpoint enforces it), so an inactive product can be edited too.</summary>
    public async Task<Result<ProductDto>> UpdateAsync(Guid id, ProductInput input, CancellationToken cancellationToken)
    {
        var product = await store.FindByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.Fail<ProductDto>(NotFound);
        }

        // No default on an edit: a missing VAT percent is an error, so an edit never resets it silently.
        var validated = ProductValidator.Validate(input, defaultVat: false);
        if (validated.IsFailure)
        {
            return Result.Fail<ProductDto>(validated.Error!);
        }

        var details = validated.Value;

        // The product's own current SKU is not a duplicate of itself.
        if (await store.SkuExistsAsync(details.Sku, product.Id, cancellationToken))
        {
            return Result.Fail<ProductDto>(SkuInUse);
        }

        var changed = product.Update(
            details.Name,
            details.Sku,
            details.Unit,
            details.UnitPrice,
            details.VatPercent,
            time.GetUtcNow().UtcDateTime);

        if (!changed)
        {
            return Result.Success(ProductDto.From(product));
        }

        return await store.TryUpdateAsync(product, cancellationToken)
            ? Result.Success(ProductDto.From(product))
            : Result.Fail<ProductDto>(SkuInUse);
    }

    /// <summary>Retires ("deactivate to hide") or restores a product. Idempotent: the state it already has changes nothing.</summary>
    public async Task<Result<ProductDto>> SetActiveAsync(Guid id, ProductActiveRequest request, CancellationToken cancellationToken)
    {
        var product = await store.FindByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.Fail<ProductDto>(NotFound);
        }

        if (request.IsActive is not { } isActive)
        {
            return Result.Fail<ProductDto>(Error.Validation(new Dictionary<string, string[]>
            {
                ["isActive"] = ["IsActive is required and must be true or false."],
            }));
        }

        if (product.SetActive(isActive, time.GetUtcNow().UtcDateTime)
            && !await store.TryUpdateAsync(product, cancellationToken))
        {
            // Only the SKU is unique, and this does not touch it: reaching here is a genuine bug.
            throw new InvalidOperationException("Changing the active state cannot violate the unique SKU index.");
        }

        return Result.Success(ProductDto.From(product));
    }

    /// <param name="includeInactive">
    /// Whether the caller may see inactive products (an Admin). When false an inactive product is reported
    /// exactly like one that does not exist, so its presence is not revealed.
    /// </param>
    public async Task<Result<ProductDto>> GetAsync(Guid id, bool includeInactive, CancellationToken cancellationToken)
    {
        var product = await store.FindByIdAsync(id, cancellationToken);
        return product is null || (!product.IsActive && !includeInactive)
            ? Result.Fail<ProductDto>(NotFound)
            : Result.Success(ProductDto.From(product));
    }

    public async Task<Result<PagedResult<ProductListItem>>> SearchAsync(ProductQuery query, bool includeInactive, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var page = 1;
        if (!string.IsNullOrWhiteSpace(query.Page)
            && (!int.TryParse(query.Page, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = ["Page must be a whole number of 1 or more."];
        }

        var pageSize = DefaultPageSize;
        if (!string.IsNullOrWhiteSpace(query.PageSize)
            && (!int.TryParse(query.PageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || pageSize is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Page size must be a whole number from 1 to {MaxPageSize}."];
        }

        var text = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        if (text is { Length: > MaxSearchLength })
        {
            errors["q"] = [$"Search text must be at most {MaxSearchLength} characters."];
        }

        ProductUnit? unit = null;
        if (!string.IsNullOrWhiteSpace(query.Unit))
        {
            if (ProductValidator.TryParseUnit(query.Unit, out var parsedUnit))
            {
                unit = parsedUnit;
            }
            else
            {
                errors["unit"] = ["Unit must be Hour, Item or Service."];
            }
        }

        // A caller who may not see inactive products always gets active ones only: whatever status they send is
        // ignored (not refused), so nothing hints that inactive products exist.
        var status = ProductStatusFilter.Active;
        if (includeInactive && !string.IsNullOrWhiteSpace(query.Status))
        {
            if (TryParseStatus(query.Status, out var parsedStatus))
            {
                status = parsedStatus;
            }
            else
            {
                errors["status"] = ["Status must be active, inactive or all."];
            }
        }

        if (errors.Count > 0)
        {
            return Result.Fail<PagedResult<ProductListItem>>(Error.Validation(errors));
        }

        var found = await store.SearchAsync(new ProductSearch(text, unit, status, page, pageSize), cancellationToken);
        return Result.Success(found.Map(ProductListItem.From));
    }

    private static bool TryParseStatus(string value, out ProductStatusFilter status)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "active":
                status = ProductStatusFilter.Active;
                return true;
            case "inactive":
                status = ProductStatusFilter.Inactive;
                return true;
            case "all":
                status = ProductStatusFilter.All;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
