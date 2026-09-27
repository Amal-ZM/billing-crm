using System.Security.Claims;
using BillingCrm.Api.Auth;
using BillingCrm.Domain;
using Microsoft.AspNetCore.Http;

namespace BillingCrm.Api.Tests;

/// <summary>
/// Who is acting (spec 010, FR-009): the signed-in user's email and role from the token's claims, and nothing else. A request cannot name
/// another user, and a missing or odd user is a defect that throws (an entry without a user must never be written).
/// </summary>
public class HttpContextCurrentUserTests
{
    private static HttpContextCurrentUser Adapter(HttpContext? context) => new(new HttpContextAccessor { HttpContext = context });

    private static DefaultHttpContext SignedIn(params (string Type, string Value)[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test", "email", "role")),
        };
        return context;
    }

    // ---- the user is the token's ----

    [Theory]
    [InlineData("agent1@crm.local", "Agent", StaffRole.Agent)]
    [InlineData("admin@crm.local", "Admin", StaffRole.Admin)]
    public void The_actor_is_the_email_and_role_in_the_token(string email, string role, StaffRole expected)
    {
        var actor = Adapter(SignedIn(("email", email), ("role", role))).Current;

        Assert.Equal(email, actor.Email);
        Assert.Equal(expected, actor.Role);
    }

    [Fact]
    public void The_email_is_used_exactly_as_it_is_in_the_token_case_included()
    {
        Assert.Equal("Agent1@CRM.local", Adapter(SignedIn(("email", "Agent1@CRM.local"), ("role", "Agent"))).Current.Email);
    }

    [Fact]
    public void Other_claims_such_as_sub_and_jti_are_not_needed()
    {
        var actor = Adapter(SignedIn(("sub", Guid.NewGuid().ToString()), ("jti", "abc"), ("email", "agent1@crm.local"), ("role", "Agent"))).Current;

        Assert.Equal("agent1@crm.local", actor.Email);
    }

    // ---- nothing in the request can name another user ----

    [Fact]
    public void A_header_a_query_string_and_a_form_naming_another_user_change_nothing()
    {
        var context = SignedIn(("email", "agent1@crm.local"), ("role", "Agent"));
        context.Request.Headers["X-Actor-Email"] = "admin@crm.local";
        context.Request.Headers["X-Actor-Role"] = "Admin";
        context.Request.Headers["email"] = "admin@crm.local";
        context.Request.QueryString = new QueryString("?email=admin@crm.local&role=Admin&actorEmail=admin@crm.local");

        var actor = Adapter(context).Current;

        Assert.Equal("agent1@crm.local", actor.Email);
        Assert.Equal(StaffRole.Agent, actor.Role);
    }

    // ---- a missing or odd user is a defect ----

    [Fact]
    public void Without_an_http_context_at_all_it_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Adapter(null).Current);
    }

    [Fact]
    public void Without_a_signed_in_user_it_throws_even_if_claims_are_present()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "agent1@crm.local"), new Claim("role", "Agent")])), // no authentication type: not authenticated
        };

        Assert.Throws<InvalidOperationException>(() => Adapter(context).Current);
    }

    [Fact]
    public void A_token_without_an_email_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Adapter(SignedIn(("role", "Agent"))).Current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_token_with_an_empty_email_throws(string email)
    {
        Assert.Throws<InvalidOperationException>(() => Adapter(SignedIn(("email", email), ("role", "Agent"))).Current);
    }

    [Fact]
    public void A_token_without_a_role_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Adapter(SignedIn(("email", "agent1@crm.local"))).Current);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("agent")]
    [InlineData("ADMIN")]
    [InlineData("1")]
    [InlineData("")]
    public void A_token_with_a_role_that_is_not_exactly_Admin_or_Agent_throws(string role)
    {
        Assert.Throws<InvalidOperationException>(() => Adapter(SignedIn(("email", "agent1@crm.local"), ("role", role))).Current);
    }

    [Fact]
    public void The_failure_message_is_fixed_text_and_never_echoes_the_token_values()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Adapter(SignedIn(("email", "secret@crm.local"), ("role", "Nope"))).Current);

        Assert.DoesNotContain("secret@crm.local", error.Message);
        Assert.DoesNotContain("Nope", error.Message);
    }
}
