using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The invoice list's four dashboard views (spec 011): status, overdue and paidMonth filters through the service, with fakes.</summary>
public class InvoiceServiceSearchFilterTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServiceSearchFilterTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, new FixedTimeProvider(Now), new FakeCurrentUser());
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now.UtcDateTime);
        _products.Seed(_consulting);
    }

    private Invoice Seed(InvoiceStatus status, string dueDate = "2030-07-01")
    {
        var invoice = Invoice.Create(_abc.Id, DateOnly.Parse(dueDate), null, Now.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Now.UtcDateTime);
        switch (status)
        {
            case InvoiceStatus.Sent:
                Assert.True(invoice.Send(Now.UtcDateTime));
                break;
            case InvoiceStatus.Cancelled:
                Assert.True(invoice.Cancel(Now.UtcDateTime));
                break;
        }

        _invoices.Seed(invoice);
        return invoice;
    }

    private Invoice SeedPaid(string paidOn)
    {
        var invoice = Seed(InvoiceStatus.Sent);
        invoice.RecordPayment(DateOnly.Parse(paidOn), 230.00m, PaymentMethod.Cash, null, Now.UtcDateTime);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        return invoice;
    }

    private async Task<List<Guid>> IdsAsync(InvoiceQuery query)
    {
        var result = await _sut.SearchAsync(query, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value.Items.Select(item => item.Id).ToList();
    }

    // ---- each filter alone ----

    [Fact]
    public async Task Status_Draft_returns_only_Draft_invoices()
    {
        var draft = Seed(InvoiceStatus.Draft);
        Seed(InvoiceStatus.Sent);
        Seed(InvoiceStatus.Cancelled);

        Assert.Equal([draft.Id], await IdsAsync(new InvoiceQuery(null, null, "Draft")));
    }

    [Fact]
    public async Task Status_Sent_returns_every_Sent_invoice_on_time_and_overdue_alike()
    {
        var onTime = Seed(InvoiceStatus.Sent, "2030-08-01");
        var overdue = Seed(InvoiceStatus.Sent, "2030-06-01");
        Seed(InvoiceStatus.Draft);

        var ids = await IdsAsync(new InvoiceQuery(null, null, "Sent"));

        Assert.Equal(new[] { onTime.Id, overdue.Id }.OrderBy(id => id), ids.OrderBy(id => id));
    }

    [Fact]
    public async Task Status_Sent_with_overdue_true_returns_only_the_overdue_ones()
    {
        Seed(InvoiceStatus.Sent, "2030-08-01");
        var overdue = Seed(InvoiceStatus.Sent, "2030-06-01");

        Assert.Equal([overdue.Id], await IdsAsync(new InvoiceQuery(null, null, "Sent", "true")));
    }

    [Fact]
    public async Task Status_Paid_with_paidMonth_true_returns_only_invoices_settled_this_month()
    {
        var thisMonth = SeedPaid("2030-06-10");
        SeedPaid("2030-05-10");

        Assert.Equal([thisMonth.Id], await IdsAsync(new InvoiceQuery(null, null, "Paid", null, "true")));
    }

    // ---- an unrecognized status ----

    [Theory]
    [InlineData("not-a-status")]
    [InlineData("draft")] // wrong case: only the exact stored name is accepted
    [InlineData("0")] // the enum's own underlying number, which Enum.TryParse alone would accept as Draft
    [InlineData("1")] // ditto, as Sent
    public async Task An_unrecognized_status_is_a_field_error_naming_status(string status)
    {
        var result = await _sut.SearchAsync(new InvoiceQuery(null, null, status), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.True(result.Error.Errors!.ContainsKey("status"));
    }

    // ---- overdue/paidMonth ignored with the wrong (or no) status ----

    [Fact]
    public async Task Overdue_paired_with_a_non_Sent_status_changes_nothing()
    {
        var draft = Seed(InvoiceStatus.Draft);

        Assert.Equal([draft.Id], await IdsAsync(new InvoiceQuery(null, null, "Draft", "true")));
    }

    [Fact]
    public async Task Overdue_with_no_status_at_all_changes_nothing()
    {
        var draft = Seed(InvoiceStatus.Draft);
        var sent = Seed(InvoiceStatus.Sent, "2030-06-01");

        var ids = await IdsAsync(new InvoiceQuery(null, null, null, "true"));

        Assert.Equal(new[] { draft.Id, sent.Id }.OrderBy(id => id), ids.OrderBy(id => id));
    }

    [Fact]
    public async Task PaidMonth_paired_with_a_non_Paid_status_changes_nothing()
    {
        var cancelled = Seed(InvoiceStatus.Cancelled);

        Assert.Equal([cancelled.Id], await IdsAsync(new InvoiceQuery(null, null, "Cancelled", null, "true")));
    }

    // ---- paging and ordering still apply within a filtered set ----

    [Fact]
    public async Task Paging_and_newest_first_ordering_are_preserved_within_a_filtered_set()
    {
        var first = Seed(InvoiceStatus.Draft);
        var second = Seed(InvoiceStatus.Draft);
        Seed(InvoiceStatus.Sent);

        var result = await _sut.SearchAsync(new InvoiceQuery("1", "1", "Draft"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Single(result.Value.Items);
        Assert.Equal(second.Id, result.Value.Items[0].Id); // newest first: second was created after first
        Assert.NotEqual(first.Id, result.Value.Items[0].Id);
    }

    // ---- no filter values at all reproduces today's behaviour exactly ----

    [Fact]
    public async Task No_filter_values_returns_every_invoice_regardless_of_status()
    {
        var draft = Seed(InvoiceStatus.Draft);
        var sent = Seed(InvoiceStatus.Sent);
        var cancelled = Seed(InvoiceStatus.Cancelled);

        var ids = await IdsAsync(new InvoiceQuery(null, null));

        Assert.Equal(new[] { draft.Id, sent.Id, cancelled.Id }.OrderBy(id => id), ids.OrderBy(id => id));
    }
}
