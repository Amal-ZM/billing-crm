using System.Reflection;
using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Application.Tests.Fakes;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>History is permanent in the application (spec 010): the store contract and the service offer no way to change or remove an entry.</summary>
public class InvoiceHistoryImmutabilityTests
{
    private static readonly string[] Destructive = ["update", "remove", "delete", "clear", "replace", "edit", "modify", "purge", "truncate", "set"];

    private static IEnumerable<MethodInfo> Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName);

    private static bool TouchesHistory(MethodInfo method) =>
        method.Name.Contains("History", StringComparison.OrdinalIgnoreCase)
        || method.GetParameters().Any(parameter => Mentions(parameter.ParameterType))
        || Mentions(method.ReturnType);

    private static bool Mentions(Type type) =>
        type == typeof(InvoiceHistoryEntry)
        || type == typeof(HistoryChange)
        || (type.IsGenericType && type.GetGenericArguments().Any(Mentions));

    [Fact]
    public void The_store_has_exactly_one_member_that_writes_history_and_one_that_reads_it()
    {
        var historyMembers = Methods(typeof(IInvoiceStore)).Where(TouchesHistory).Select(method => method.Name).OrderBy(name => name).ToList();

        Assert.Equal(["ListHistoryAsync", "RecordHistory"], historyMembers);
        var record = typeof(IInvoiceStore).GetMethod(nameof(IInvoiceStore.RecordHistory))!;
        Assert.Equal(typeof(void), record.ReturnType);
        Assert.Equal([typeof(InvoiceHistoryEntry)], record.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void No_store_member_is_named_to_update_remove_delete_clear_or_replace_anything_about_history()
    {
        foreach (var method in Methods(typeof(IInvoiceStore)).Where(TouchesHistory))
        {
            Assert.DoesNotContain(Destructive, word => method.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        // Nothing else takes or returns an entry either: the two members above are the whole surface.
        Assert.Equal(2, Methods(typeof(IInvoiceStore)).Count(TouchesHistory));
    }

    [Fact]
    public void The_service_offers_only_the_read_of_history()
    {
        var names = Methods(typeof(InvoiceService)).Where(TouchesHistory).Select(method => method.Name).ToList();

        Assert.Equal([nameof(InvoiceService.GetHistoryAsync)], names);
    }

    [Fact]
    public void The_history_dto_types_are_read_shapes_with_no_way_to_write_anything_back()
    {
        foreach (var type in new[] { typeof(InvoiceHistoryDto), typeof(HistoryEntryDto), typeof(HistoryChangeDto) })
        {
            Assert.All(type.GetProperties(), property => Assert.False(property.SetMethod is { IsPublic: true } setter && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(), $"{type.Name}.{property.Name} has a public setter"));
        }
    }

    [Fact]
    public async Task The_fakes_history_list_is_a_copy_so_changing_it_does_not_change_what_is_stored()
    {
        var store = new FakeInvoiceStore();
        var invoiceId = Guid.NewGuid();
        var actor = new Actor("agent1@crm.local", StaffRole.Agent);
        store.RecordHistory(InvoiceHistoryBuilder.Sent(invoiceId, actor, DateTime.UtcNow));
        await store.SaveChangesAsync(CancellationToken.None);

        var listed = await store.ListHistoryAsync(invoiceId, CancellationToken.None);
        (listed as IList<InvoiceHistoryEntry>)?.Clear();
        var committed = store.HistoryOf(invoiceId);
        (committed as IList<InvoiceHistoryEntry>)?.Clear();

        Assert.Single(await store.ListHistoryAsync(invoiceId, CancellationToken.None));
        Assert.Single(store.HistoryOf(invoiceId));
    }

    [Fact]
    public async Task Reading_history_through_the_service_never_records_or_saves()
    {
        var store = new FakeInvoiceStore();
        var customers = new FakeCustomerStore();
        var products = new FakeProductStore();
        var sut = new InvoiceService(store, customers, products, new FixedTimeProvider(DateTimeOffset.UtcNow), new FakeCurrentUser());
        var customer = Customer.Create("ABC", CustomerType.Company, "a@abc.example", "0500000000", null, "Riyadh", DateTime.UtcNow);
        var product = Product.Create("Consulting", "CNS-1", ProductUnit.Hour, 100m, 15m, DateTime.UtcNow);
        var invoice = Invoice.Create(customer.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, DateTime.UtcNow);
        invoice.AddLine(product, 1m, DateTime.UtcNow);
        store.Seed(invoice);

        await sut.GetHistoryAsync(invoice.Id, CancellationToken.None);
        await sut.GetHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, store.SaveChangesCalls);
        Assert.Equal(0, store.AddCalls);
        Assert.Equal(0, store.PendingHistoryCount);
        Assert.Empty(store.HistoryOf(invoice.Id));
    }
}
