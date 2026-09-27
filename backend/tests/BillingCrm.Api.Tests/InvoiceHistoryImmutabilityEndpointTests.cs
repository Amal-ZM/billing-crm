using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingCrm.Domain;

namespace BillingCrm.Api.Tests;

/// <summary>History is permanent over HTTP too (spec 010): nothing the API offers can write, change or remove an entry, whoever asks.</summary>
public class InvoiceHistoryImmutabilityEndpointTests
{
    private static readonly DateTime Now = new(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static string Day(int daysFromToday) => DateTime.UtcNow.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private sealed record World(ApiFactory Factory, Customer Abc, Product Consulting);

    private static World NewWorld()
    {
        var factory = new ApiFactory();
        var abc = Customer.Create("ABC", CustomerType.Company, "accounts@abc.example", "0500000000", null, "Riyadh", Now);
        factory.CustomerStore.Seed(abc);
        var consulting = Product.Create("Consulting hour", "CNS-001", ProductUnit.Hour, 100m, 15m, Now);
        factory.ProductStore.Seed(consulting);
        return new World(factory, abc, consulting);
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, World world)
    {
        var body = $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"Net 30\",\"lines\":[{{\"productId\":\"{world.Consulting.Id}\",\"quantity\":2}}]}}";
        var response = await client.PostAsync("/api/invoices", Json(body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A deep copy of an invoice's entries: every field, so a later comparison catches any change to any of them.</summary>
    private static List<string> Copy(World world, Guid id) =>
        world.Factory.InvoiceStore.HistoryOf(id)
            .Select(entry => $"{entry.Id}|{entry.Sequence}|{entry.InvoiceId}|{entry.Kind}|{entry.OccurredAtUtc:O}|{entry.ActorEmail}|{entry.ActorRole}|" +
                             string.Join(";", entry.Changes.Select(change => $"{change.Field}={change.Before}>{change.After}")))
            .ToList();

    [Theory]
    [InlineData("Agent", "POST")]
    [InlineData("Agent", "PUT")]
    [InlineData("Agent", "PATCH")]
    [InlineData("Agent", "DELETE")]
    [InlineData("Admin", "POST")]
    [InlineData("Admin", "PUT")]
    [InlineData("Admin", "PATCH")]
    [InlineData("Admin", "DELETE")]
    public async Task Writing_to_the_history_address_or_to_one_entry_is_never_a_success_and_changes_nothing(string role, string method)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient(role);
        var created = await CreateAsync(client, world);
        var id = created.GetProperty("id").GetGuid();
        var entryId = factory.InvoiceStore.HistoryOf(id).Single().Id;
        var before = Copy(world, id);

        foreach (var address in new[] { $"/api/invoices/{id}/history", $"/api/invoices/{id}/history/{entryId}", $"/api/invoices/{id}/history/{Guid.NewGuid()}" })
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), address) { Content = Json("{\"kind\":\"Sent\",\"actorEmail\":\"x@crm.test\"}") });

            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{role} {method} {address} answered {(int)response.StatusCode}");
        }

        Assert.Equal(before, Copy(world, id));
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Reading_a_single_entry_or_the_history_of_all_invoices_is_not_offered(string role)
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient(role);
        var id = (await CreateAsync(client, world)).GetProperty("id").GetGuid();
        var entryId = factory.InvoiceStore.HistoryOf(id).Single().Id;

        foreach (var address in new[] { $"/api/invoices/{id}/history/{entryId}", "/api/invoices/history", "/api/history", $"/api/history/{entryId}" })
        {
            var response = await client.GetAsync(address);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"GET {address} answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Reading_history_changes_nothing()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var client = factory.CreateAuthorizedClient("Agent");
        var id = (await CreateAsync(client, world)).GetProperty("id").GetGuid();
        var before = Copy(world, id);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/invoices/{id}/history")).StatusCode);
        }

        Assert.Equal(before, Copy(world, id));
    }

    [Fact]
    public async Task Creating_editing_sending_paying_and_cancelling_further_leave_the_earlier_entries_equal_to_a_copy_taken_before()
    {
        var world = NewWorld();
        await using var factory = world.Factory;
        using var agent = factory.CreateAuthorizedClient("Agent");
        using var admin = factory.CreateAuthorizedClient("Admin");
        var created = await CreateAsync(agent, world);
        var id = created.GetProperty("id").GetGuid();
        var lineId = created.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var copies = new List<List<string>> { Copy(world, id) };

        var edit = $"{{\"customerId\":\"{world.Abc.Id}\",\"dueDate\":\"{Day(30)}\",\"notes\":\"changed\",\"lines\":[{{\"id\":\"{lineId}\",\"quantity\":3}}]}}";
        Assert.Equal(HttpStatusCode.OK, (await agent.PutAsync($"/api/invoices/{id}", Json(edit))).StatusCode);
        copies.Add(Copy(world, id));
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync($"/api/invoices/{id}/send", null)).StatusCode);
        copies.Add(Copy(world, id));
        var today = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync($"/api/invoices/{id}/payments", Json($"{{\"date\":\"{today}\",\"amount\":100,\"method\":\"Cash\"}}"))).StatusCode);
        copies.Add(Copy(world, id));

        var other = (await CreateAsync(agent, world)).GetProperty("id").GetGuid();
        var otherBefore = Copy(world, other);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/invoices/{other}/cancel", null)).StatusCode);

        for (var i = 1; i < copies.Count; i++)
        {
            Assert.Equal(copies[i - 1], copies[i].Take(copies[i - 1].Count).ToList());
            Assert.Equal(copies[i - 1].Count + 1, copies[i].Count);
        }

        Assert.Equal(otherBefore, Copy(world, other).Take(otherBefore.Count).ToList());
        Assert.Equal(otherBefore.Count + 1, Copy(world, other).Count);
    }
}
