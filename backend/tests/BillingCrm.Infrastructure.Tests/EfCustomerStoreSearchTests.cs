using BillingCrm.Application.Customers;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Customers;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// Search semantics that only a real SQL Server can prove: LIKE matching, escaping, collation, Arabic.
/// The database is shared by the tests in this class, so every test isolates its rows with its own marker.
/// </summary>
public class EfCustomerStoreSearchTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Hex characters only, so it can never contain a LIKE wildcard.</summary>
    private static string NewMarker() => Guid.NewGuid().ToString("N")[..12];

    private static string Digits() => Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();

    private async Task<IReadOnlyList<Customer>> AddAsync(params Customer[] customers)
    {
        await using var context = database.CreateContext();
        var store = new EfCustomerStore(context);
        foreach (var customer in customers)
        {
            Assert.True(await store.TryAddAsync(customer, default), $"could not add {customer.Name}");
        }

        return customers;
    }

    private static Customer Make(
        string name,
        string? email = null,
        CustomerType type = CustomerType.Company,
        string? phone = null,
        string? vat = null,
        bool active = true)
    {
        var customer = Customer.Create(
            name,
            type,
            email ?? $"{Guid.NewGuid():N}@example.test",
            phone ?? $"05{Digits()}",
            vat,
            "Somewhere",
            Now);
        if (!active)
        {
            customer.SetActive(false, Now);
        }

        return customer;
    }

    private async Task<string[]> NamesAsync(CustomerSearch search)
    {
        await using var context = database.CreateContext();
        var page = await new EfCustomerStore(context).SearchAsync(search, default);
        return page.Items.Select(customer => customer.Name).ToArray();
    }

    private static CustomerSearch Search(string? text = null, CustomerType? type = null, bool? active = null, int page = 1, int pageSize = 100) =>
        new(text, type, active, page, pageSize);

    [Fact]
    public async Task Name_matches_by_contains_ignoring_case()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Acme Trading {marker}"), Make($"Unrelated {marker}"));

        Assert.Equal(new[] { $"Acme Trading {marker}" }, await NamesAsync(Search($"acme trading {marker.ToUpperInvariant()}")));
        Assert.Equal(new[] { $"Acme Trading {marker}" }, await NamesAsync(Search($"CME TRADING {marker}")));
    }

    [Fact]
    public async Task Email_phone_and_vat_number_are_searched_too()
    {
        var marker = NewMarker();
        var digits = Digits();
        var customer = Make($"Fields {marker}", email: $"Contact.{marker}@Example.Test", phone: $"+966 {digits}", vat: $"VAT-{marker}");
        await AddAsync(customer, Make($"Other {marker}"));

        Assert.Equal(new[] { customer.Name }, await NamesAsync(Search($"contact.{marker.ToUpperInvariant()}@EXAMPLE")));
        Assert.Equal(new[] { customer.Name }, await NamesAsync(Search($"+966 {digits}")));
        Assert.Equal(new[] { customer.Name }, await NamesAsync(Search($"vat-{marker.ToUpperInvariant()}")));
    }

    [Fact]
    public async Task Percent_is_matched_literally_not_as_a_wildcard()
    {
        var marker = NewMarker();
        await AddAsync(Make($"100% Cotton {marker}"), Make($"100X Cotton {marker}"), Make($"1000 Cotton {marker}"));

        Assert.Equal(new[] { $"100% Cotton {marker}" }, await NamesAsync(Search($"100% Cotton {marker}")));
    }

    [Fact]
    public async Task Underscore_is_matched_literally_not_as_a_single_character_wildcard()
    {
        var marker = NewMarker();
        await AddAsync(Make($"a_b {marker}"), Make($"aXb {marker}"));

        Assert.Equal(new[] { $"a_b {marker}" }, await NamesAsync(Search($"a_b {marker}")));
    }

    [Fact]
    public async Task Square_brackets_are_matched_literally_not_as_a_character_class()
    {
        var marker = NewMarker();
        await AddAsync(Make($"[abc] {marker}"), Make($"a {marker}"), Make($"b {marker}"));

        Assert.Equal(new[] { $"[abc] {marker}" }, await NamesAsync(Search($"[abc] {marker}")));
    }

    [Fact]
    public async Task Backslash_is_matched_literally()
    {
        var marker = NewMarker();
        await AddAsync(Make($"a\\b {marker}"), Make($"ab {marker}"));

        Assert.Equal(new[] { $"a\\b {marker}" }, await NamesAsync(Search($"a\\b {marker}")));
    }

    [Fact]
    public async Task An_arabic_substring_matches_an_arabic_name()
    {
        var marker = NewMarker();
        await AddAsync(Make($"شركة الأمل للتجارة {marker}"), Make($"شركة النور {marker}"));

        Assert.Equal(new[] { $"شركة الأمل للتجارة {marker}" }, await NamesAsync(Search($"الأمل للتجارة {marker}")));
        Assert.Equal(2, (await NamesAsync(Search($"شركة"))).Count(name => name.EndsWith(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Mixed_arabic_and_english_text_is_searchable_in_either_script()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Al-Amal الأمل Trading {marker}"));

        Assert.Single(await NamesAsync(Search($"al-amal الأمل trading {marker}")));
        Assert.Single(await NamesAsync(Search($"الأمل Trading {marker}")));
    }

    [Fact]
    public async Task A_blank_search_text_applies_no_text_filter()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Blank A {marker}"), Make($"Blank B {marker}"));

        Assert.True((await NamesAsync(Search("   "))).Count(name => name.EndsWith(marker, StringComparison.Ordinal)) == 2);
    }

    [Fact]
    public async Task The_type_filter_limits_the_results()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"Company {marker}", type: CustomerType.Company),
            Make($"Person {marker}", type: CustomerType.Individual));

        Assert.Equal(new[] { $"Person {marker}" }, await NamesAsync(Search(marker, type: CustomerType.Individual)));
        Assert.Equal(new[] { $"Company {marker}" }, await NamesAsync(Search(marker, type: CustomerType.Company)));
    }

    [Fact]
    public async Task The_active_filter_limits_the_results_and_no_filter_returns_both()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Live {marker}"), Make($"Dormant {marker}", active: false));

        Assert.Equal(new[] { $"Live {marker}" }, await NamesAsync(Search(marker, active: true)));
        Assert.Equal(new[] { $"Dormant {marker}" }, await NamesAsync(Search(marker, active: false)));
        Assert.Equal(new[] { $"Dormant {marker}", $"Live {marker}" }, await NamesAsync(Search(marker)));
    }

    [Fact]
    public async Task Text_type_and_status_filters_combine()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"Match {marker}", type: CustomerType.Individual, active: false),
            Make($"Wrong type {marker}", type: CustomerType.Company, active: false),
            Make($"Wrong status {marker}", type: CustomerType.Individual, active: true),
            Make($"Wrong text {Guid.NewGuid():N}", type: CustomerType.Individual, active: false));

        Assert.Equal(
            new[] { $"Match {marker}" },
            await NamesAsync(Search($"match {marker}", CustomerType.Individual, active: false)));
    }

    [Fact]
    public async Task Paging_is_stable_even_when_names_tie_and_reports_totals()
    {
        var marker = NewMarker();
        var customers = Enumerable.Range(0, 5).Select(_ => Make($"Same Name {marker}")).ToArray();
        await AddAsync(customers);

        var seen = new List<Guid>();
        int totalCount = 0, totalPages = 0;
        for (var page = 1; page <= 3; page++)
        {
            await using var context = database.CreateContext();
            var result = await new EfCustomerStore(context).SearchAsync(Search(marker, page: page, pageSize: 2), default);
            seen.AddRange(result.Items.Select(customer => customer.Id));
            totalCount = result.TotalCount;
            totalPages = result.TotalPages;
        }

        Assert.Equal(5, totalCount);
        Assert.Equal(3, totalPages);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(customers.Select(customer => customer.Id).OrderBy(id => id), seen.OrderBy(id => id));
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_but_reports_the_real_total()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Only {marker}"));

        await using var context = database.CreateContext();
        var result = await new EfCustomerStore(context).SearchAsync(Search(marker, page: 9, pageSize: 20), default);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task No_match_gives_zero_totals()
    {
        await using var context = database.CreateContext();

        var result = await new EfCustomerStore(context).SearchAsync(Search($"no-such-{NewMarker()}"), default);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }
}
