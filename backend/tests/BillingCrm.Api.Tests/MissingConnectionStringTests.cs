namespace BillingCrm.Api.Tests;

public class MissingConnectionStringTests
{
    [Fact]
    public void Startup_fails_with_a_message_naming_the_missing_setting_and_no_connection_details()
    {
        using var factory = new ApiFactory(connectionString: null);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message);
        Assert.DoesNotContain("Server=", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_treats_a_blank_connection_string_as_missing()
    {
        using var factory = new ApiFactory(connectionString: "   ");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message);
    }
}
