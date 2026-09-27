namespace BillingCrm.Domain;

/// <summary>How money was received (spec 008). Exactly these three; another method needs a later spec. It is a record of what staff were told, not a way to charge anything.</summary>
public enum PaymentMethod
{
    Cash,
    Bank,
    Other,
}
