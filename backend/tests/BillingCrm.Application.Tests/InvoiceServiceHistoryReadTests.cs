using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>Reading an invoice's history through the service (spec 010): newest first, what it says, whether it starts with creation, and that reading writes nothing.</summary>
public class InvoiceServiceHistoryReadTests
{
    private static readonly DateTimeOffset Start = new(2030, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeInvoiceStore _invoices = new();
    private readonly FakeCustomerStore _customers = new();
    private readonly FakeProductStore _products = new();
    private readonly FixedTimeProvider _time = new(Start);
    private readonly FakeCurrentUser _user = new();
    private readonly InvoiceService _sut;

    private readonly Customer _abc;
    private readonly Product _consulting;

    public InvoiceServiceHistoryReadTests()
    {
        _sut = new InvoiceService(_invoices, _customers, _products, _time, _user);
        _abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Start.UtcDateTime);
        _customers.Seed(_abc);
        _consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Start.UtcDateTime);
        _products.Seed(_consulting);
    }

    private static InvoiceHistoryDto Ok(Result<InvoiceHistoryDto> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value;
    }

    private async Task<InvoiceDto> CreateAsync() => (await _sut.CreateAsync(
        new InvoiceInput(_abc.Id.ToString(), "2030-07-01", "Net 30", [new InvoiceLineInput(null, _consulting.Id.ToString(), 2m)]),
        CancellationToken.None)).Value;

    private Invoice SeededSent()
    {
        var invoice = Invoice.Create(_abc.Id, new DateOnly(2030, 7, 1), "Net 30", Start.UtcDateTime);
        invoice.AddLine(_consulting, 2m, Start.UtcDateTime);
        Assert.True(invoice.Send(Start.UtcDateTime));
        _invoices.Seed(invoice);
        return invoice;
    }

    [Fact]
    public async Task History_has_the_invoice_number_as_text_and_the_entries_newest_first()
    {
        var created = await CreateAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await _sut.SendAsync(created.Id, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _sut.RecordPaymentAsync(created.Id, new PaymentInput("2030-06-01", 100m, "Cash", null), CancellationToken.None);

        var history = Ok(await _sut.GetHistoryAsync(created.Id, CancellationToken.None));

        Assert.Equal("INV-000001", history.Number);
        Assert.Equal(["Payment", "Sent", "Created"], history.Entries.Select(entry => entry.Kind));

        _time.Advance(TimeSpan.FromMinutes(1));
        await _sut.RecordPaymentAsync(created.Id, new PaymentInput("2030-06-01", 130m, "Bank", null), CancellationToken.None);
        var later = Ok(await _sut.GetHistoryAsync(created.Id, CancellationToken.None));

        Assert.Equal(["Payment", "Payment", "Sent", "Created"], later.Entries.Select(entry => entry.Kind));
        Assert.Equal(history.Entries[0].Id, later.Entries[1].Id);
    }

    [Fact]
    public async Task Two_entries_committed_in_one_save_keep_the_order_they_were_recorded_in_newest_first()
    {
        var invoice = SeededSent();
        var actor = new Application.Auth.Actor("agent1@crm.local", StaffRole.Agent);
        var first = InvoiceHistoryBuilder.Sent(invoice.Id, actor, Start.UtcDateTime);
        var second = InvoiceHistoryBuilder.Cancelled(invoice.Id, InvoiceStatus.Sent, actor, Start.UtcDateTime);
        _invoices.RecordHistory(first);
        _invoices.RecordHistory(second);
        await _invoices.SaveChangesAsync(CancellationToken.None);

        var history = Ok(await _sut.GetHistoryAsync(invoice.Id, CancellationToken.None));

        Assert.Equal([second.Id, first.Id], history.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task An_entry_reads_with_its_kind_as_a_name_the_user_the_time_and_its_changes_with_nulls_kept()
    {
        var created = await CreateAsync();

        var history = Ok(await _sut.GetHistoryAsync(created.Id, CancellationToken.None));

        var entry = Assert.Single(history.Entries);
        Assert.Equal("Created", entry.Kind);
        Assert.Equal("agent1@crm.local", entry.ActorEmail);
        Assert.Equal("Agent", entry.ActorRole);
        Assert.Equal(Start.UtcDateTime, entry.OccurredAtUtc);
        Assert.Contains(new HistoryChangeDto("Customer", null, "ABC"), entry.Changes);
        Assert.Contains(new HistoryChangeDto("Due date", null, "2030-07-01"), entry.Changes);
    }

    [Fact]
    public async Task StartsWithCreation_is_true_when_a_Created_entry_exists()
    {
        var created = await CreateAsync();

        Assert.True(Ok(await _sut.GetHistoryAsync(created.Id, CancellationToken.None)).StartsWithCreation);
    }

    [Fact]
    public async Task StartsWithCreation_is_false_and_entries_are_empty_for_an_invoice_with_no_history()
    {
        var invoice = SeededSent();

        var history = Ok(await _sut.GetHistoryAsync(invoice.Id, CancellationToken.None));

        Assert.False(history.StartsWithCreation);
        Assert.Empty(history.Entries);
        Assert.Equal("INV-000001", history.Number);
    }

    [Fact]
    public async Task StartsWithCreation_is_false_when_the_only_entries_are_a_Payment_and_a_Cancelled_or_Sent()
    {
        var invoice = SeededSent();
        await _sut.RecordPaymentAsync(invoice.Id, new PaymentInput("2030-06-01", 100m, "Cash", null), CancellationToken.None);

        var history = Ok(await _sut.GetHistoryAsync(invoice.Id, CancellationToken.None));

        Assert.Equal(["Payment"], history.Entries.Select(entry => entry.Kind));
        Assert.False(history.StartsWithCreation);
    }

    [Fact]
    public async Task An_unknown_id_is_invoice_not_found()
    {
        var result = await _sut.GetHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal(InvoiceService.NotFound, result.Error);
    }

    [Fact]
    public async Task Reading_history_writes_and_records_nothing()
    {
        var created = await CreateAsync();
        var saves = _invoices.SaveChangesCalls;
        var adds = _invoices.AddCalls;

        await _sut.GetHistoryAsync(created.Id, CancellationToken.None);
        await _sut.GetHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(saves, _invoices.SaveChangesCalls);
        Assert.Equal(adds, _invoices.AddCalls);
        Assert.Equal(0, _invoices.PendingHistoryCount);
        Assert.Single(_invoices.HistoryOf(created.Id));
    }

    [Fact]
    public async Task Reading_history_does_not_need_or_use_the_signed_in_user()
    {
        var created = await CreateAsync();
        _user.Missing = true;

        var history = Ok(await _sut.GetHistoryAsync(created.Id, CancellationToken.None));

        Assert.Single(history.Entries);
    }
}
