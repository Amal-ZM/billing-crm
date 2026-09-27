using BillingCrm.Application.Products;
using BillingCrm.Domain;
using BillingCrm.Infrastructure.Products;

namespace BillingCrm.Infrastructure.Tests;

/// <summary>
/// Search semantics that only a real SQL Server can prove: LIKE matching, escaping, collation, Arabic.
/// The database is shared by the tests in this class, so every test isolates its rows with its own marker.
/// </summary>
public class EfProductStoreSearchTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Hex characters only (upper-case, as a SKU is stored), so it can never contain a LIKE wildcard.</summary>
    private static string NewMarker() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task AddAsync(params Product[] products)
    {
        await using var context = database.CreateContext();
        var store = new EfProductStore(context);
        foreach (var product in products)
        {
            Assert.True(await store.TryAddAsync(product, default), $"could not add {product.Name}");
        }
    }

    private static Product Make(string name, string sku, ProductUnit unit = ProductUnit.Item, bool active = true)
    {
        var product = Product.Create(name, sku, unit, 10m, 15m, Now);
        if (!active)
        {
            product.SetActive(false, Now);
        }

        return product;
    }

    private async Task<string[]> NamesAsync(ProductSearch search)
    {
        await using var context = database.CreateContext();
        var page = await new EfProductStore(context).SearchAsync(search, default);
        return page.Items.Select(product => product.Name).ToArray();
    }

    private static ProductSearch Search(
        string? text = null,
        ProductUnit? unit = null,
        ProductStatusFilter status = ProductStatusFilter.All,
        int page = 1,
        int pageSize = 100) => new(text, unit, status, page, pageSize);

    [Fact]
    public async Task Name_matches_by_contains_ignoring_case()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Consulting hour {marker}", $"CNS-{marker}"), Make($"Unrelated {marker}", $"UNR-{marker}"));

        Assert.Equal(new[] { $"Consulting hour {marker}" }, await NamesAsync(Search($"consulting hour {marker.ToLowerInvariant()}")));
        Assert.Equal(new[] { $"Consulting hour {marker}" }, await NamesAsync(Search($"ONSULTING HOUR {marker}")));
    }

    [Fact]
    public async Task Sku_matches_by_contains_ignoring_case()
    {
        var marker = NewMarker();
        await AddAsync(Make($"First {marker}", $"CNS-{marker}"), Make($"Second {marker}", $"WEB-{marker}"));

        Assert.Equal(new[] { $"First {marker}" }, await NamesAsync(Search($"cns-{marker.ToLowerInvariant()}")));
        Assert.Equal(new[] { $"Second {marker}" }, await NamesAsync(Search($"web-{marker}")));
        Assert.Equal(2, (await NamesAsync(Search(marker.ToLowerInvariant()))).Length);
    }

    [Fact]
    public async Task Percent_is_matched_literally_not_as_a_wildcard()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"100% Cotton {marker}", $"A-{marker}"),
            Make($"100X Cotton {marker}", $"B-{marker}"),
            Make($"1000 Cotton {marker}", $"C-{marker}"));

        Assert.Equal(new[] { $"100% Cotton {marker}" }, await NamesAsync(Search($"100% Cotton {marker}")));
    }

    [Fact]
    public async Task Underscore_is_matched_literally_in_a_name_and_in_a_sku()
    {
        var marker = NewMarker();
        await AddAsync(Make($"a_b {marker}", $"A_B-{marker}"), Make($"aXb {marker}", $"AXB-{marker}"));

        Assert.Equal(new[] { $"a_b {marker}" }, await NamesAsync(Search($"a_b {marker}")));
        Assert.Equal(new[] { $"a_b {marker}" }, await NamesAsync(Search($"a_b-{marker}")));
    }

    [Fact]
    public async Task Square_brackets_and_backslashes_are_matched_literally()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"[abc] {marker}", $"BR-{marker}"),
            Make($"a {marker}", $"A-{marker}"),
            Make($"a\\b {marker}", $"BS-{marker}"),
            Make($"ab {marker}", $"AB-{marker}"));

        Assert.Equal(new[] { $"[abc] {marker}" }, await NamesAsync(Search($"[abc] {marker}")));
        Assert.Equal(new[] { $"a\\b {marker}" }, await NamesAsync(Search($"a\\b {marker}")));
    }

    [Fact]
    public async Task An_arabic_substring_matches_an_arabic_name()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"ساعة استشارة {marker}", $"AR1-{marker}"),
            Make($"باقة دعم {marker}", $"AR2-{marker}"));

        Assert.Equal(new[] { $"ساعة استشارة {marker}" }, await NamesAsync(Search($"استشارة {marker}")));
        Assert.Equal(2, (await NamesAsync(Search(marker))).Length);
    }

    [Fact]
    public async Task A_blank_search_text_applies_no_text_filter()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Blank A {marker}", $"BA-{marker}"), Make($"Blank B {marker}", $"BB-{marker}"));

        Assert.True((await NamesAsync(Search("   "))).Count(name => name.EndsWith(marker, StringComparison.Ordinal)) == 2);
    }

    [Fact]
    public async Task The_unit_filter_limits_the_results()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"Hourly {marker}", $"H-{marker}", ProductUnit.Hour),
            Make($"Thing {marker}", $"I-{marker}", ProductUnit.Item),
            Make($"Work {marker}", $"S-{marker}", ProductUnit.Service));

        Assert.Equal(new[] { $"Hourly {marker}" }, await NamesAsync(Search(marker, ProductUnit.Hour)));
        Assert.Equal(new[] { $"Thing {marker}" }, await NamesAsync(Search(marker, ProductUnit.Item)));
        Assert.Equal(new[] { $"Work {marker}" }, await NamesAsync(Search(marker, ProductUnit.Service)));
    }

    [Fact]
    public async Task The_status_filter_limits_the_results()
    {
        var marker = NewMarker();
        await AddAsync(Make($"Live {marker}", $"L-{marker}"), Make($"Dormant {marker}", $"D-{marker}", active: false));

        Assert.Equal(new[] { $"Live {marker}" }, await NamesAsync(Search(marker, status: ProductStatusFilter.Active)));
        Assert.Equal(new[] { $"Dormant {marker}" }, await NamesAsync(Search(marker, status: ProductStatusFilter.Inactive)));
        Assert.Equal(new[] { $"Dormant {marker}", $"Live {marker}" }, await NamesAsync(Search(marker, status: ProductStatusFilter.All)));
    }

    [Fact]
    public async Task Text_unit_and_status_filters_combine()
    {
        var marker = NewMarker();
        await AddAsync(
            Make($"Match {marker}", $"M-{marker}", ProductUnit.Service, active: false),
            Make($"Wrong unit {marker}", $"WU-{marker}", ProductUnit.Item, active: false),
            Make($"Wrong status {marker}", $"WS-{marker}", ProductUnit.Service, active: true),
            Make($"Wrong text {Guid.NewGuid():N}", $"WT-{Guid.NewGuid():N}"[..30].ToUpperInvariant(), ProductUnit.Service, active: false));

        Assert.Equal(
            new[] { $"Match {marker}" },
            await NamesAsync(Search($"match {marker}", ProductUnit.Service, ProductStatusFilter.Inactive)));
    }

    [Fact]
    public async Task Paging_is_stable_even_when_names_tie_and_reports_totals()
    {
        var marker = NewMarker();
        var products = Enumerable.Range(0, 5).Select(n => Make($"Same Name {marker}", $"T{n}-{marker}")).ToArray();
        await AddAsync(products);

        var seen = new List<Guid>();
        int totalCount = 0, totalPages = 0;
        for (var page = 1; page <= 3; page++)
        {
            await using var context = database.CreateContext();
            var result = await new EfProductStore(context).SearchAsync(Search(marker, page: page, pageSize: 2), default);
            seen.AddRange(result.Items.Select(product => product.Id));
            totalCount = result.TotalCount;
            totalPages = result.TotalPages;
        }

        Assert.Equal(5, totalCount);
        Assert.Equal(3, totalPages);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(products.Select(product => product.Id).OrderBy(id => id), seen.OrderBy(id => id));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public async Task A_page_beyond_the_end_is_empty_but_reports_the_real_total(int page)
    {
        var marker = NewMarker();
        await AddAsync(Make($"Only {marker}", $"O-{marker}"));

        await using var context = database.CreateContext();
        var result = await new EfProductStore(context).SearchAsync(Search(marker, page: page, pageSize: 100), default);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task No_match_gives_zero_totals()
    {
        await using var context = database.CreateContext();

        var result = await new EfProductStore(context).SearchAsync(Search($"no-such-{NewMarker()}"), default);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }
}
