namespace BillingCrm.Application.Auth;

/// <summary>Both fields are nullable because they come straight from the request body.</summary>
public sealed record SignInRequest(string? Email, string? Password);

public sealed record SignInResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, string Email, string Role);
