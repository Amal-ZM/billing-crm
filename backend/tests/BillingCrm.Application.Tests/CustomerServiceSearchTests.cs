using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class CustomerServiceSearchTests
{
    private readonly FakeCustomerStore _store = new();
    private readonly CustomerService _sut;

    public CustomerServiceSearchTests()
    {
        _sut = new CustomerService(_store, new FixedTimeProvider(new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    private static CustomerQuery Query(string? q = null, string? type = null, string? active = null, string? page = null, string? pageSize = null) =>
        new(q, type, active, page, pageSize);

    private async Task<IReadOnlyDictionary<string, string[]>> ErrorsOfAsync(CustomerQuery query)
    {
        var result = await _sut.SearchAsync(query, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Null(_store.LastSearch);
        return result.Error.Errors!;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("+1")]
    public async Task An_invalid_page_is_a_validation_failure_naming_page(string page) =>
        Assert.Contains("page", (await ErrorsOfAsync(Query(page: page))).Keys);

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("many")]
    public async Task An_invalid_page_size_is_a_validation_failure_naming_pageSize(string pageSize) =>
        Assert.Contains("pageSize", (await ErrorsOfAsync(Query(pageSize: pageSize))).Keys);

    [Theory]
    [InlineData("Partnership")]
    [InlineData("1")]
    [InlineData("0")]
    public async Task An_unknown_type_is_a_validation_failure_naming_type(string type) =>
        Assert.Contains("type", (await ErrorsOfAsync(Query(type: type))).Keys);

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("active")]
    public async Task A_non_boolean_active_is_a_validation_failure_naming_active(string active) =>
        Assert.Contains("active", (await ErrorsOfAsync(Query(active: active))).Keys);

    [Fact]
    public async Task Several_bad_values_are_all_reported_together()
    {
        var errors = await ErrorsOfAsync(Query(type: "x", active: "x", page: "0", pageSize: "0"));

        Assert.Equal(new[] { "active", "page", "pageSize", "type" }, errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task With_no_values_it_asks_the_store_for_page_1_of_20_with_no_filters()
    {
        var result = await _sut.SearchAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CustomerSearch(null, null, null, 1, 20), _store.LastSearch);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("3", "100")]
    [InlineData("250", "50")]
    public async Task Valid_paging_is_passed_through(string page, string pageSize)
    {
        await _sut.SearchAsync(Query(page: page, pageSize: pageSize), CancellationToken.None);

        Assert.Equal(int.Parse(page), _store.LastSearch!.Page);
        Assert.Equal(int.Parse(pageSize), _store.LastSearch.PageSize);
    }

    [Theory]
    [InlineData("Company", CustomerType.Company)]
    [InlineData("company", CustomerType.Company)]
    [InlineData(" INDIVIDUAL ", CustomerType.Individual)]
    public async Task Type_is_parsed_ignoring_case_and_spaces(string type, CustomerType expected)
    {
        await _sut.SearchAsync(Query(type: type), CancellationToken.None);

        Assert.Equal(expected, _store.LastSearch!.Type);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(" False ", false)]
    public async Task Active_is_parsed_as_a_boolean(string active, bool expected)
    {
        await _sut.SearchAsync(Query(active: active), CancellationToken.None);

        Assert.Equal(expected, _store.LastSearch!.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_type_and_active_mean_no_filter(string? blank)
    {
        await _sut.SearchAsync(Query(type: blank, active: blank), CancellationToken.None);

        Assert.Null(_store.LastSearch!.Type);
        Assert.Null(_store.LastSearch.IsActive);
    }

    [Theory]
    [InlineData("  acme  ", "acme")]
    [InlineData("شركة", "شركة")]
    public async Task Search_text_is_trimmed(string q, string expected)
    {
        await _sut.SearchAsync(Query(q: q), CancellationToken.None);

        Assert.Equal(expected, _store.LastSearch!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_search_text_means_no_search(string? q)
    {
        await _sut.SearchAsync(Query(q: q), CancellationToken.None);

        Assert.Null(_store.LastSearch!.Text);
    }

    [Fact]
    public async Task Results_are_mapped_to_list_items_with_the_active_state_and_totals()
    {
        _store.Seed(Customer.Create("Acme", CustomerType.Company, "a@x.test", "0500000000", "V-1", "Somewhere", DateTime.UtcNow));
        var dormant = Customer.Create("Dormant", CustomerType.Individual, "d@x.test", "0500000001", null, "Elsewhere", DateTime.UtcNow);
        dormant.SetActive(false, DateTime.UtcNow);
        _store.Seed(dormant);

        var result = await _sut.SearchAsync(Query(), CancellationToken.None);

        Assert.Equal(2, result.Value.TotalCount);
        Assert.Collection(
            result.Value.Items,
            first =>
            {
                Assert.Equal("Acme", first.Name);
                Assert.Equal("Company", first.Type);
                Assert.Equal("V-1", first.VatNumber);
                Assert.True(first.IsActive);
            },
            second =>
            {
                Assert.Equal("Dormant", second.Name);
                Assert.Null(second.VatNumber);
                Assert.False(second.IsActive);
            });
    }
}
