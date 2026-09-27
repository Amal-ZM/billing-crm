using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class InvoiceServiceSearchTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly InvoiceService _sut;

    public InvoiceServiceSearchTests()
    {
        _sut = new InvoiceService(_invoices, new FakeCustomerStore(), new FakeProductStore(), new FixedTimeProvider(Now), new FakeCurrentUser());
    }

    private static InvoiceQuery Query(string? page = null, string? pageSize = null) => new(page, pageSize);

    private async Task<IReadOnlyDictionary<string, string[]>> ErrorsOf(InvoiceQuery query)
    {
        var result = await _sut.SearchAsync(query, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData(" 2")]
    [InlineData("+2")]
    public async Task A_bad_page_is_a_field_error(string page)
    {
        Assert.True((await ErrorsOf(Query(page: page))).ContainsKey("page"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("abc")]
    public async Task A_bad_page_size_is_a_field_error(string pageSize)
    {
        Assert.True((await ErrorsOf(Query(pageSize: pageSize))).ContainsKey("pageSize"));
    }

    [Fact]
    public async Task Both_bad_values_are_named_together()
    {
        var errors = await ErrorsOf(Query("0", "500"));

        Assert.Equal(["page", "pageSize"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Null(_invoices.LastSearch);
    }

    [Fact]
    public async Task Without_values_the_defaults_are_page_1_of_20()
    {
        var result = await _sut.SearchAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((1, 20), _invoices.LastSearch);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("3", "100")]
    [InlineData("2147483647", "20")]
    public async Task Valid_values_reach_the_store_as_given(string page, string pageSize)
    {
        var result = await _sut.SearchAsync(Query(page, pageSize), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((int.Parse(page), int.Parse(pageSize)), _invoices.LastSearch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_values_mean_the_defaults(string blank)
    {
        var result = await _sut.SearchAsync(Query(blank, blank), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((1, 20), _invoices.LastSearch);
    }

    [Fact]
    public async Task Rows_become_summaries_with_the_formatted_number_the_status_the_customers_name_and_the_due_date_as_text()
    {
        var customer = Customer.Create("شركة الأمل", CustomerType.Company, "a@amal.example", "0500000000", null, "Riyadh", Now.UtcDateTime);
        var product = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now.UtcDateTime);
        var invoice = Invoice.Create(customer.Id, new DateOnly(2030, 7, 1), null, Now.UtcDateTime);
        invoice.AddLine(product, 2m, Now.UtcDateTime);
        _invoices.CustomerNames[customer.Id] = customer.Name;
        _invoices.Seed(invoice);

        var result = await _sut.SearchAsync(Query(), CancellationToken.None);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal(invoice.Id, item.Id);
        Assert.Equal("INV-000001", item.Number);
        Assert.Equal("Draft", item.Status);
        Assert.Equal(customer.Id, item.CustomerId);
        Assert.Equal("شركة الأمل", item.CustomerName);
        Assert.Equal("2030-07-01", item.DueDate);
        Assert.Equal(Now.UtcDateTime, item.CreatedAtUtc);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
    }

    [Fact]
    public async Task The_summary_carries_no_lines_and_no_totals()
    {
        var names = typeof(InvoiceSummary).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Line", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Total", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Balance", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task No_invoices_gives_an_empty_page_with_zero_totals()
    {
        var result = await _sut.SearchAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(0, result.Value.TotalPages);
    }
}
