using System.Globalization;
using BillingCrm.Application.Common;
using BillingCrm.Application.Invoices;

namespace BillingCrm.Application.Tests;

public class InvoiceValidatorTests
{
    private static readonly DateOnly Today = new(2030, 6, 15);
    private static readonly string Customer = Guid.NewGuid().ToString();

    private static string NewProduct() => Guid.NewGuid().ToString();

    private static InvoiceLineInput NewLine(decimal? quantity = 2m, string? productId = null) => new(null, productId ?? NewProduct(), quantity);

    private static InvoiceInput Valid(
        string? customerId = null,
        string? dueDate = "2030-07-01",
        string? notes = null,
        IReadOnlyList<InvoiceLineInput>? lines = null) =>
        new(customerId ?? Customer, dueDate, notes, lines ?? [NewLine()]);

    private static IReadOnlyDictionary<string, string[]> ErrorsOfCreate(InvoiceInput input)
    {
        var result = InvoiceValidator.ForCreate(input, Today);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    private static IReadOnlyDictionary<string, string[]> ErrorsOfEdit(InvoiceInput input, DateOnly? currentDueDate = null)
    {
        var result = InvoiceValidator.ForEdit(input, Today, currentDueDate ?? new DateOnly(2030, 7, 1));
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        return result.Error.Errors!;
    }

    [Fact]
    public void A_valid_input_passes_and_is_converted()
    {
        var product = NewProduct();

        var result = InvoiceValidator.ForCreate(Valid(notes: "Net 30", lines: [NewLine(2m, product)]), Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse(Customer), result.Value.CustomerId);
        Assert.Equal(new DateOnly(2030, 7, 1), result.Value.DueDate);
        Assert.Equal("Net 30", result.Value.Notes);
        var line = Assert.Single(result.Value.Lines);
        Assert.Null(line.ExistingId);
        Assert.Equal(Guid.Parse(product), line.ProductId);
        Assert.Equal(2m, line.Quantity);
    }

    // ---- customer ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_customer_is_required(string? customerId)
    {
        Assert.True(ErrorsOfCreate(Valid(customerId: customerId ?? string.Empty) with { CustomerId = customerId }).ContainsKey("customerId"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    public void A_malformed_customer_id_is_a_field_error_not_a_crash(string customerId)
    {
        Assert.True(ErrorsOfCreate(Valid(customerId: customerId)).ContainsKey("customerId"));
    }

    // ---- due date ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_due_date_is_required(string? dueDate)
    {
        Assert.True(ErrorsOfCreate(Valid() with { DueDate = dueDate }).ContainsKey("dueDate"));
    }

    [Theory]
    [InlineData("15/06/2030")]
    [InlineData("2030-6-15")]
    [InlineData("2030-06-15T00:00:00")]
    [InlineData("June 15 2030")]
    [InlineData("2030-02-30")]
    [InlineData("2030-13-01")]
    [InlineData("0000-01-01")]
    [InlineData("٢٠٣٠-٠٦-٢٠")]
    public void A_due_date_must_be_a_real_date_in_the_form_yyyy_MM_dd(string dueDate)
    {
        var errors = ErrorsOfCreate(Valid(dueDate: dueDate));

        Assert.Contains("yyyy-MM-dd", errors["dueDate"][0]);
    }

    [Fact]
    public void Today_is_accepted_and_yesterday_is_refused_on_create()
    {
        Assert.True(InvoiceValidator.ForCreate(Valid(dueDate: "2030-06-15"), Today).IsSuccess);
        Assert.Equal("Due date cannot be in the past.", ErrorsOfCreate(Valid(dueDate: "2030-06-14"))["dueDate"][0]);
    }

    [Fact]
    public void A_leap_day_is_a_real_date()
    {
        Assert.True(InvoiceValidator.ForCreate(Valid(dueDate: "2032-02-29"), Today).IsSuccess);
        Assert.True(ErrorsOfCreate(Valid(dueDate: "2031-02-29")).ContainsKey("dueDate"));
    }

    [Fact]
    public void On_edit_an_unchanged_past_due_date_is_accepted_but_a_changed_past_date_is_not()
    {
        var pastCurrent = new DateOnly(2030, 6, 1);

        var unchanged = InvoiceValidator.ForEdit(Valid(dueDate: "2030-06-01", lines: [NewLine()]), Today, pastCurrent);
        var changedToOtherPast = InvoiceValidator.ForEdit(Valid(dueDate: "2030-05-31"), Today, pastCurrent);
        var changedToFuture = InvoiceValidator.ForEdit(Valid(dueDate: "2030-06-30"), Today, pastCurrent);

        Assert.True(unchanged.IsSuccess);
        Assert.True(changedToOtherPast.IsFailure);
        Assert.True(changedToOtherPast.Error!.Errors!.ContainsKey("dueDate"));
        Assert.True(changedToFuture.IsSuccess);
    }

    // ---- notes ----

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Net 30  ", "Net 30")]
    [InlineData("Line one\nLine two", "Line one\nLine two")]
    [InlineData("ملاحظات الدفع", "ملاحظات الدفع")]
    public void Notes_are_trimmed_empty_means_none_and_line_breaks_and_arabic_are_kept(string? notes, string? expected)
    {
        var result = InvoiceValidator.ForCreate(Valid(notes: notes), Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Notes);
    }

    [Fact]
    public void Notes_of_exactly_1000_characters_are_accepted_and_1001_are_refused()
    {
        Assert.True(InvoiceValidator.ForCreate(Valid(notes: new string('n', 1000)), Today).IsSuccess);
        Assert.True(ErrorsOfCreate(Valid(notes: new string('n', 1001))).ContainsKey("notes"));
    }

    [Fact]
    public void The_notes_limit_counts_the_trimmed_text()
    {
        Assert.True(InvoiceValidator.ForCreate(Valid(notes: "  " + new string('n', 1000) + "  "), Today).IsSuccess);
    }

    [Fact]
    public void Notes_containing_a_bidi_override_character_is_refused_on_the_notes_field()
    {
        var errors = ErrorsOfCreate(Valid(notes: "Net 30‮days"));

        Assert.Contains("notes", errors.Keys);
        Assert.Contains("Notes contains a character that is not allowed.", errors["notes"]);
    }

    // ---- lines ----

    [Fact]
    public void At_least_one_line_is_required()
    {
        Assert.True(ErrorsOfCreate(Valid(lines: [])).ContainsKey("lines"));
    }

    [Fact]
    public void A_missing_lines_list_is_a_lines_error()
    {
        Assert.True(ErrorsOfCreate(Valid() with { Lines = null }).ContainsKey("lines"));
    }

    [Fact]
    public void One_and_one_hundred_lines_are_accepted_and_101_are_refused()
    {
        InvoiceLineInput[] Lines(int n) => Enumerable.Range(0, n).Select(_ => NewLine()).ToArray();

        Assert.True(InvoiceValidator.ForCreate(Valid(lines: Lines(1)), Today).IsSuccess);
        Assert.True(InvoiceValidator.ForCreate(Valid(lines: Lines(100)), Today).IsSuccess);
        Assert.True(ErrorsOfCreate(Valid(lines: Lines(101))).ContainsKey("lines"));
    }

    [Fact]
    public void On_edit_an_empty_list_is_refused_so_the_last_line_cannot_be_removed()
    {
        Assert.True(ErrorsOfEdit(Valid(lines: [])).ContainsKey("lines"));
    }

    // ---- quantity ----

    [Theory]
    [InlineData("2")]
    [InlineData("1.5")]
    [InlineData("1.50")]
    [InlineData("1.500")]
    [InlineData("0.01")]
    [InlineData("999999.99")]
    public void A_valid_quantity_is_accepted_and_kept_exactly(string text)
    {
        var quantity = decimal.Parse(text, CultureInfo.InvariantCulture);

        var result = InvoiceValidator.ForCreate(Valid(lines: [NewLine(quantity)]), Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(quantity, result.Value.Lines[0].Quantity);
    }

    [Theory]
    [InlineData("2", "2.00")]
    [InlineData("1.5", "1.50")]
    [InlineData("1.500", "1.50")]
    public void An_accepted_quantity_comes_back_with_exactly_two_decimals(string typed, string expected)
    {
        var quantity = decimal.Parse(typed, CultureInfo.InvariantCulture);

        var result = InvoiceValidator.ForCreate(Valid(lines: [NewLine(quantity)]), Today);

        Assert.Equal(expected, result.Value.Lines[0].Quantity.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("1.005")]
    [InlineData("1000000")]
    [InlineData("999999.995")]
    public void A_quantity_that_is_zero_negative_too_precise_or_too_large_is_refused_and_never_rounded(string text)
    {
        var quantity = decimal.Parse(text, CultureInfo.InvariantCulture);

        Assert.True(ErrorsOfCreate(Valid(lines: [NewLine(quantity)])).ContainsKey("lines[0].quantity"));
    }

    [Fact]
    public void A_missing_quantity_is_a_field_error_and_the_message_names_the_rule()
    {
        var errors = ErrorsOfCreate(Valid(lines: [NewLine(null)]));

        Assert.Equal("Quantity must be a number greater than zero with at most two decimal places.", errors["lines[0].quantity"][0]);
    }

    // ---- line identity ----

    [Fact]
    public void A_new_line_needs_a_valid_product_id()
    {
        var errors = ErrorsOfCreate(Valid(lines: [new InvoiceLineInput(null, null, 1m), new InvoiceLineInput(null, "nope", 1m), new InvoiceLineInput(null, "  ", 1m)]));

        Assert.True(errors.ContainsKey("lines[0].productId"));
        Assert.True(errors.ContainsKey("lines[1].productId"));
        Assert.True(errors.ContainsKey("lines[2].productId"));
    }

    [Fact]
    public void A_line_id_is_not_allowed_when_creating()
    {
        var errors = ErrorsOfCreate(Valid(lines: [new InvoiceLineInput(Guid.NewGuid().ToString(), null, 1m)]));

        Assert.True(errors.ContainsKey("lines[0].id"));
    }

    [Fact]
    public void On_edit_an_existing_line_needs_only_a_valid_id_and_a_new_line_only_a_product()
    {
        var existing = Guid.NewGuid();
        var product = Guid.NewGuid();

        var result = InvoiceValidator.ForEdit(
            Valid(lines: [new InvoiceLineInput(existing.ToString(), null, 3m), new InvoiceLineInput(null, product.ToString(), 1m)]),
            Today,
            new DateOnly(2030, 7, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(existing, result.Value.Lines[0].ExistingId);
        Assert.Null(result.Value.Lines[0].ProductId);
        Assert.Null(result.Value.Lines[1].ExistingId);
        Assert.Equal(product, result.Value.Lines[1].ProductId);
    }

    [Fact]
    public void A_line_with_both_an_id_and_a_product_or_with_neither_is_refused_on_edit()
    {
        var both = new InvoiceLineInput(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1m);
        var neither = new InvoiceLineInput(null, null, 1m);

        var errors = ErrorsOfEdit(Valid(lines: [both, neither]));

        Assert.True(errors.ContainsKey("lines[0].id"));
        Assert.True(errors.ContainsKey("lines[1].productId"));
    }

    [Fact]
    public void A_malformed_line_id_is_a_field_error_on_edit()
    {
        Assert.True(ErrorsOfEdit(Valid(lines: [new InvoiceLineInput("not-a-guid", null, 1m)])).ContainsKey("lines[0].id"));
    }

    [Fact]
    public void The_same_existing_line_twice_is_refused_at_the_second_occurrence()
    {
        var id = Guid.NewGuid().ToString();

        var errors = ErrorsOfEdit(Valid(lines: [new InvoiceLineInput(id, null, 1m), new InvoiceLineInput(id.ToUpperInvariant(), null, 2m)]));

        Assert.False(errors.ContainsKey("lines[0].id"));
        Assert.True(errors.ContainsKey("lines[1].id"));
    }

    [Fact]
    public void The_same_product_on_two_new_lines_is_allowed()
    {
        var product = NewProduct();

        var result = InvoiceValidator.ForCreate(Valid(lines: [NewLine(1m, product), NewLine(2m, product)]), Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    // ---- the discount (spec 006) ----

    private const string DiscountRule = "Discount must be a number of 0 or more with at most two decimal places.";

    private static decimal Amount(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Fact]
    public void No_discount_means_zero()
    {
        var result = InvoiceValidator.ForCreate(Valid() with { Discount = null }, Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.00m, result.Value.Discount);
    }

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("0.00", "0.00")]
    [InlineData("20", "20.00")]
    [InlineData("20.00", "20.00")]
    [InlineData("20.1", "20.10")]
    [InlineData("20.100", "20.10")]
    [InlineData("0.01", "0.01")]
    [InlineData("9999999999999.99", "9999999999999.99")]
    public void A_discount_from_zero_up_to_the_ceiling_with_at_most_two_decimals_is_accepted_and_kept_at_two_decimals(string typed, string expected)
    {
        var result = InvoiceValidator.ForCreate(Valid() with { Discount = Amount(typed) }, Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Discount.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-1")]
    [InlineData("20.001")]
    [InlineData("0.005")]
    [InlineData("1.999")]
    [InlineData("9999999999999.995")]
    public void A_negative_discount_or_one_with_more_than_two_decimals_is_refused_never_rounded(string typed)
    {
        var errors = ErrorsOfCreate(Valid() with { Discount = Amount(typed) });

        Assert.Equal([DiscountRule], errors["discount"]);
    }

    [Theory]
    [InlineData("10000000000000")]
    [InlineData("10000000000000.00")]
    [InlineData("79228162514264337593543950335")]
    public void A_discount_above_the_ceiling_is_refused_on_the_discount_with_the_ceiling_message(string typed)
    {
        var errors = ErrorsOfCreate(Valid() with { Discount = Amount(typed) });

        Assert.Equal(["Discount cannot be more than 9,999,999,999,999.99."], errors["discount"]);
    }

    [Fact]
    public void A_discount_just_above_the_ceiling_says_what_the_limit_is()
    {
        var errors = ErrorsOfCreate(Valid() with { Discount = 10_000_000_000_000m });

        Assert.Equal(["Discount cannot be more than 9,999,999,999,999.99."], errors["discount"]);
    }

    [Fact]
    public void The_discount_is_carried_by_the_inspection_and_the_valid_invoice_for_create_and_edit()
    {
        var input = Valid() with { Discount = 20.10m };

        Assert.Equal(20.10m, InvoiceValidator.InspectForCreate(input, Today).Discount);
        Assert.Equal(20.10m, InvoiceValidator.InspectForEdit(input, Today, new DateOnly(2030, 7, 1)).Discount);
        Assert.Equal(20.10m, InvoiceValidator.ForEdit(input, Today, new DateOnly(2030, 7, 1)).Value.Discount);
    }

    [Fact]
    public void A_bad_discount_is_reported_together_with_the_other_problems()
    {
        var errors = ErrorsOfEdit(Valid(customerId: "bad", dueDate: null) with { Discount = -5m });

        Assert.Equal(["customerId", "discount", "dueDate"], errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    // ---- everything together ----

    [Fact]
    public void Several_problems_are_reported_together_with_camel_case_array_path_keys()
    {
        var errors = ErrorsOfCreate(new InvoiceInput(
            "bad",
            "2000-01-01",
            new string('n', 1001),
            [new InvoiceLineInput(null, "bad", 0m), new InvoiceLineInput(null, NewProduct(), 1.005m)]));

        Assert.Equal(
            ["customerId", "dueDate", "lines[0].productId", "lines[0].quantity", "lines[1].quantity", "notes"],
            errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }
}
