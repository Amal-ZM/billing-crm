using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

public class AuthServiceTests
{
    private static readonly DateTimeOffset FixedExpiry = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private readonly FakeHasher _hasher = new();
    private readonly FakeTokenService _tokens = new(FixedExpiry);
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _store.Add(StaffUser.Create("Admin@Example.Test", _hasher.Hash("correct-password"), StaffRole.Admin));
        _sut = new AuthService(_store, _hasher, _tokens);
    }

    [Fact]
    public async Task Correct_credentials_return_token_expiry_email_and_role()
    {
        var result = await _sut.SignInAsync(new SignInRequest("Admin@Example.Test", "correct-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("token-for-Admin@Example.Test", result.Value.AccessToken);
        Assert.Equal(FixedExpiry, result.Value.ExpiresAtUtc);
        Assert.Equal("Admin@Example.Test", result.Value.Email);
        Assert.Equal("Admin", result.Value.Role);
    }

    [Fact]
    public async Task Email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        var result = await _sut.SignInAsync(new SignInRequest("  ADMIN@example.TEST  ", "correct-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Admin@Example.Test", result.Value.Email);
    }

    [Fact]
    public async Task Wrong_password_fails_with_the_generic_unauthorized_error_and_issues_no_token()
    {
        var result = await _sut.SignInAsync(new SignInRequest("Admin@Example.Test", "wrong"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Unauthorized, result.Error!.Kind);
        Assert.Equal("Invalid credentials", result.Error.Title);
        Assert.Equal("Email or password is incorrect.", result.Error.Detail);
        Assert.Equal(0, _tokens.IssueCount);
    }

    [Fact]
    public async Task Unknown_email_fails_with_an_error_identical_to_a_wrong_password_and_still_burns_a_hash_verification()
    {
        var wrongPassword = await _sut.SignInAsync(new SignInRequest("Admin@Example.Test", "wrong"), CancellationToken.None);
        _hasher.VerifiedHashes.Clear();

        var unknownEmail = await _sut.SignInAsync(new SignInRequest("nobody@example.test", "anything"), CancellationToken.None);

        Assert.True(unknownEmail.IsFailure);
        Assert.Equal(wrongPassword.Error, unknownEmail.Error);
        Assert.Equal(new[] { _hasher.DummyHash }, _hasher.VerifiedHashes);
        Assert.Equal(0, _tokens.IssueCount);
    }

    [Theory]
    [InlineData(null, "pw", "email")]
    [InlineData("", "pw", "email")]
    [InlineData("   ", "pw", "email")]
    [InlineData("a@b.test", null, "password")]
    [InlineData("a@b.test", "", "password")]
    public async Task Missing_field_is_a_validation_failure_naming_the_field_and_nothing_is_looked_up(
        string? email, string? password, string expectedField)
    {
        var result = await _sut.SignInAsync(new SignInRequest(email, password), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains(expectedField, result.Error.Errors!.Keys);
        Assert.Equal(0, _store.LookupCount);
        Assert.Empty(_hasher.VerifiedHashes);
    }

    [Fact]
    public async Task An_oversized_email_or_password_is_a_validation_failure_and_nothing_is_looked_up_or_hashed()
    {
        var result = await _sut.SignInAsync(
            new SignInRequest(new string('a', AuthService.MaxEmailLength + 1), new string('p', AuthService.MaxPasswordLength + 1)),
            CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(new[] { "email", "password" }, result.Error.Errors!.Keys.OrderBy(key => key));
        Assert.Equal(0, _store.LookupCount);
        Assert.Empty(_hasher.VerifiedHashes);
    }

    [Fact]
    public async Task Values_exactly_at_the_limits_are_accepted_for_verification()
    {
        var result = await _sut.SignInAsync(
            new SignInRequest(new string('a', AuthService.MaxEmailLength), new string('p', AuthService.MaxPasswordLength)),
            CancellationToken.None);

        Assert.Equal(ErrorKind.Unauthorized, result.Error!.Kind);
        Assert.Equal(1, _store.LookupCount);
    }

    [Fact]
    public async Task Both_fields_missing_reports_both()
    {
        var result = await _sut.SignInAsync(new SignInRequest(null, null), CancellationToken.None);

        Assert.Equal(new[] { "email", "password" }, result.Error!.Errors!.Keys.OrderBy(key => key));
    }

    private sealed class FakeStore : IStaffUserStore
    {
        private readonly Dictionary<string, StaffUser> _users = new();

        public int LookupCount { get; private set; }

        public void Add(StaffUser user) => _users[user.NormalizedEmail] = user;

        public Task<StaffUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            LookupCount++;
            return Task.FromResult(_users.GetValueOrDefault(normalizedEmail));
        }

        public Task AddAsync(StaffUser user, CancellationToken cancellationToken)
        {
            Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public List<string> VerifiedHashes { get; } = [];

        public string DummyHash => "dummy-hash";

        public string Hash(string password) => "hashed:" + password;

        public bool Verify(string passwordHash, string password)
        {
            VerifiedHashes.Add(passwordHash);
            return passwordHash == "hashed:" + password;
        }
    }

    private sealed class FakeTokenService(DateTimeOffset expiry) : ITokenService
    {
        public int IssueCount { get; private set; }

        public IssuedToken Issue(StaffUser user)
        {
            IssueCount++;
            return new IssuedToken("token-for-" + user.Email, expiry);
        }
    }
}
