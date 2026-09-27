using BillingCrm.Infrastructure.Auth;

namespace BillingCrm.Api.Tests;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void A_hash_verifies_only_for_the_original_password()
    {
        var hash = _hasher.Hash("correct-horse");

        Assert.True(_hasher.Verify(hash, "correct-horse"));
        Assert.False(_hasher.Verify(hash, "wrong-horse"));
    }

    [Fact]
    public void The_hash_never_contains_the_password_and_is_salted()
    {
        var first = _hasher.Hash("correct-horse");
        var second = _hasher.Hash("correct-horse");

        Assert.DoesNotContain("correct-horse", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_dummy_hash_is_a_real_hash_that_no_normal_password_matches()
    {
        Assert.False(_hasher.Verify(_hasher.DummyHash, "anything"));
        Assert.False(_hasher.Verify(_hasher.DummyHash, string.Empty));
        Assert.Equal(_hasher.DummyHash, _hasher.DummyHash);
    }
}
