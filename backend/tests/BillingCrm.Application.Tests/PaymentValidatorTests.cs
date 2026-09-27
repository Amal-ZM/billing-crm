using System.Globalization;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;
using BillingCrm.Domain;

namespace BillingCrm.Application.Tests;

/// <summary>The shape rules for a payment (spec 008, contracts/payments-api.md "Field rules"): checked, never adjusted, all problems together.</summary>
public class PaymentValidatorTests
{
    private static readonly DateOnly Today = new(2030, 6, 15);
    private static readonly DateOnly SentOn = new(2030, 6, 1);
    private const decimal Remaining = 230.00m;

    private static PaymentInput Valid(string? date = "2030-06-10", decimal? amount = 100.00m, string? method = "Cash", string? note = null) =>
        new(date, amount, method, note);

    private static Result<ValidPayment> Run(PaymentInput input, decimal remaining = Remaining, DateOnly? sentOn = null, DateOnly? today = null) =>
        PaymentValidator.Validate(input, today ?? Today, sentOn ?? SentOn, remaining);

    private static IReadOnlyDictionary<string, string[]> Errors(PaymentInput input, decimal remaining = Remaining, DateOnly? sentOn = null, DateOnly? today = null)
    {
        var result = Run(input, remaining, sentOn, today);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    // ---- a valid payment ----

    [Fact]
    public void A_valid_payment_passes_and_is_converted()
    {
        var result = Run(Valid(note: "First half"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2030, 6, 10), result.Value.PaidOn);
        Assert.Equal(100.00m, result.Value.Amount);
        Assert.Equal(PaymentMethod.Cash, result.Value.Method);
        Assert.Equal("First half", result.Value.Note);
    }

    [Theory]
    [InlineData("Cash", PaymentMethod.Cash)]
    [InlineData("Bank", PaymentMethod.Bank)]
    [InlineData("Other", PaymentMethod.Other)]
    public void The_three_methods_are_accepted(string method, PaymentMethod expected)
    {
        Assert.Equal(expected, Run(Valid(method: method)).Value.Method);
    }

    // ---- amount ----

    [Fact]
    public void A_missing_amount_is_refused()
    {
        Assert.Equal([PaymentValidator.AmountRequired], Errors(Valid(amount: null))["amount"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    public void A_zero_or_negative_amount_is_refused(string amount)
    {
        Assert.Equal([PaymentValidator.AmountRule], Errors(Valid(amount: decimal.Parse(amount, CultureInfo.InvariantCulture)))["amount"]);
    }

    [Theory]
    [InlineData("10.005")]
    [InlineData("0.001")]
    [InlineData("100.999")]
    public void An_amount_with_a_third_decimal_place_is_refused_never_rounded(string amount)
    {
        Assert.Equal([PaymentValidator.AmountRule], Errors(Valid(amount: decimal.Parse(amount, CultureInfo.InvariantCulture)))["amount"]);
    }

    [Fact]
    public void An_amount_with_trailing_zeros_beyond_two_places_is_not_a_third_decimal()
    {
        // 100.100 is the same number as 100.10.
        var result = Run(Valid(amount: 100.100m));

        Assert.True(result.IsSuccess);
        Assert.Equal(100.10m, result.Value.Amount);
        Assert.Equal(2, result.Value.Amount.Scale);
    }

    [Fact]
    public void An_amount_above_the_remaining_is_refused_naming_how_much_remains()
    {
        var errors = Errors(Valid(amount: 130.01m), remaining: 130.00m);

        Assert.Equal(["Amount cannot be more than the remaining 130.00 SAR."], errors["amount"]);
    }

    [Fact]
    public void Exactly_the_remaining_amount_is_accepted()
    {
        Assert.True(Run(Valid(amount: 130.00m), remaining: 130.00m).IsSuccess);
    }

    [Fact]
    public void One_halala_is_accepted_when_one_halala_remains()
    {
        Assert.True(Run(Valid(amount: 0.01m), remaining: 0.01m).IsSuccess);
    }

    [Fact]
    public void Nothing_can_be_paid_when_nothing_remains()
    {
        Assert.Equal(["Amount cannot be more than the remaining 0.00 SAR."], Errors(Valid(amount: 0.01m), remaining: 0.00m)["amount"]);
    }

    [Fact]
    public void The_remaining_is_shown_with_two_decimals_and_no_thousands_separator_from_the_machines_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal(["Amount cannot be more than the remaining 1234.50 SAR."], Errors(Valid(amount: 2000m), remaining: 1234.5m)["amount"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_malformed_amount_does_not_also_get_the_overpayment_message()
    {
        var errors = Errors(Valid(amount: 10.005m), remaining: 5.00m);

        Assert.Equal([PaymentValidator.AmountRule], errors["amount"]);
    }

    // ---- date ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_date_is_refused(string? date)
    {
        Assert.Equal([PaymentValidator.DateRequired], Errors(Valid(date: date))["date"]);
    }

    [Theory]
    [InlineData("2030-02-30")]
    [InlineData("2030-13-01")]
    [InlineData("15/06/2030")]
    [InlineData("2030-6-1")]
    [InlineData("not a date")]
    public void A_date_that_is_not_a_real_yyyy_MM_dd_date_is_refused(string date)
    {
        Assert.Equal([PaymentValidator.DateFormat], Errors(Valid(date: date))["date"]);
    }

    [Fact]
    public void Today_in_Saudi_Arabia_is_accepted_and_tomorrow_is_refused()
    {
        Assert.True(Run(Valid(date: "2030-06-15")).IsSuccess);
        Assert.Equal([PaymentValidator.DateInTheFuture], Errors(Valid(date: "2030-06-16"))["date"]);
    }

    [Fact]
    public void The_day_the_invoice_was_sent_is_accepted_and_the_day_before_is_refused()
    {
        Assert.True(Run(Valid(date: "2030-06-01")).IsSuccess);
        Assert.Equal(["Date cannot be before the invoice was sent (2030-06-01)."], Errors(Valid(date: "2030-05-31"))["date"]);
    }

    [Fact]
    public void The_sent_date_check_is_skipped_when_the_sent_date_is_unknown()
    {
        Assert.True(PaymentValidator.Validate(Valid(date: "2000-01-01"), Today, null, Remaining).IsSuccess);
    }

    // ---- method ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_missing_method_is_refused(string? method)
    {
        Assert.Equal([PaymentValidator.MethodRequired], Errors(Valid(method: method))["method"]);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("BANK")]
    [InlineData("Card")]
    [InlineData("Cheque")]
    [InlineData("1")]
    public void Only_the_exact_names_Cash_Bank_and_Other_are_accepted(string method)
    {
        Assert.Equal([PaymentValidator.MethodRule], Errors(Valid(method: method))["method"]);
    }

    // ---- note ----

    [Fact]
    public void A_note_is_optional_and_an_empty_one_becomes_none()
    {
        Assert.Null(Run(Valid(note: null)).Value.Note);
        Assert.Null(Run(Valid(note: "")).Value.Note);
        Assert.Null(Run(Valid(note: "   ")).Value.Note);
    }

    [Fact]
    public void A_note_is_trimmed_and_kept_as_typed_including_Arabic_and_line_breaks()
    {
        Assert.Equal("دفعة أولى\nشكراً", Run(Valid(note: "  دفعة أولى\nشكراً  ")).Value.Note);
    }

    [Fact]
    public void A_note_of_1000_characters_is_accepted_and_1001_is_refused()
    {
        Assert.True(Run(Valid(note: new string('x', 1000))).IsSuccess);
        Assert.Equal([PaymentValidator.NoteTooLong], Errors(Valid(note: new string('x', 1001)))["note"]);
    }

    [Fact]
    public void The_note_limit_is_the_same_as_the_invoice_notes_limit()
    {
        Assert.Equal(InvoiceValidator.MaxNotes, PaymentValidator.MaxNote);
    }

    [Fact]
    public void Spaces_around_a_note_do_not_count_towards_the_limit()
    {
        Assert.True(Run(Valid(note: "  " + new string('x', 1000) + "  ")).IsSuccess);
    }

    [Fact]
    public void A_note_containing_a_bidi_override_character_is_refused_on_the_note_field()
    {
        var errors = Errors(Valid(note: "Cash‮payment"));

        Assert.Contains("note", errors.Keys);
        Assert.Contains("Note contains a character that is not allowed.", errors["note"]);
    }

    // ---- everything together ----

    [Fact]
    public void All_the_problems_are_reported_together()
    {
        var errors = Errors(new PaymentInput("2030-06-99", 10.005m, "Card", new string('x', 1001)));

        Assert.Equal(["date", "amount", "method", "note"], errors.Keys.OrderBy(key => Array.IndexOf(new[] { "date", "amount", "method", "note" }, key)));
    }

    [Fact]
    public void A_completely_empty_body_reports_date_amount_and_method()
    {
        var errors = Errors(new PaymentInput(null, null, null, null));

        Assert.Equal(["amount", "date", "method"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }
}
