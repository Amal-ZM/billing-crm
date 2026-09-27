namespace BillingCrm.Application.Tests.Fakes;

/// <summary>A clock the test controls, so timestamps can be asserted exactly.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
