using System.Globalization;
using BillingCrm.Application.Auth;
using BillingCrm.Application.Common;
using BillingCrm.Application.Customers;
using BillingCrm.Application.Products;
using BillingCrm.Domain;

namespace BillingCrm.Application.Invoices;

/// <summary>
/// Creates, reads, edits, sends and cancels invoices, and records payments against Sent ones (spec 008). Every rule lives here and in <see cref="InvoiceValidator"/>: the request is checked as a whole
/// (shape, then the customer, then each new line's product), all problems are reported together, and nothing is saved unless
/// there are none. The amounts (line net and VAT, the totals) are calculated by <see cref="InvoiceMoney"/> from what each line saved and are returned with the invoice. They are calculated while the invoice is a Draft and frozen into their own columns when it leaves Draft (send, or cancel), after which they are read back, never recalculated.
/// Every successful change (create, edit, send, payment, cancel) also records one history entry (spec 010) for the signed-in user, added to the same save as
/// the change (<see cref="IInvoiceStore.RecordHistory"/>): an entry exists exactly when its change was saved, and a refused, unchanged or lost-race request
/// writes none.
/// </summary>
public sealed class InvoiceService(IInvoiceStore invoices, ICustomerStore customers, IProductStore products, TimeProvider time, ICurrentUser currentUser)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Saudi Arabia is UTC+03:00 all year (no daylight saving), so a fixed offset gives its calendar date without a time-zone database.</summary>
    private static readonly TimeSpan SaudiOffset = TimeSpan.FromHours(3);

    public static readonly Error NotFound = Error.NotFound(
        "invoice_not_found",
        "Invoice not found",
        "No invoice exists with this id.");

    /// <summary>
    /// Two people saved the same Draft at once and the other save got there first in a way this one cannot be applied on top of
    /// (for example it removed a line this one was changing). Nothing of this save was written; the person reloads and tries again.
    /// </summary>
    public static readonly Error ChangedByAnotherPerson = Error.Conflict(
        "invoice_changed",
        "Invoice changed",
        "This invoice was changed by someone else. Reload it and try again.");

    /// <summary>The message for an unknown or inactive customer: it is fixed text, so it never says which case it was.</summary>
    public const string ChooseActiveCustomer = "Choose an active customer.";

    /// <summary>The message for an unknown or inactive product.</summary>
    public const string ChooseActiveProduct = "This product is inactive or does not exist. Choose an active product.";

    /// <summary>A discount is taken off the net, so it cannot be more than the net of the lines the save would produce.</summary>
    public const string DiscountAboveNet = "Discount cannot be more than the total net of the lines.";

    /// <summary>One line's net (quantity times its price) is above <see cref="InvoiceMoney.MaxAmount"/>.</summary>
    public const string LineAmountTooLarge = "The amount of this line is too large. Reduce the quantity.";

    /// <summary>The invoice's total net or grand total would be above <see cref="InvoiceMoney.MaxAmount"/>.</summary>
    public const string InvoiceTotalTooLarge = "The invoice total is too large. Reduce the quantities or remove lines.";

    /// <summary>A Draft whose due date has passed cannot be sent: the person changes the due date first.</summary>
    public const string DueDateInThePastToSend = "Due date is in the past. Change the due date before sending.";

    /// <summary>The requested change of status is not one of the legal ones for the invoice's current status (a 409 naming it).</summary>
    public static Error IllegalChange(string shownStatus, string verb) => Error.Conflict(
        "invoice_status_change_not_allowed",
        "Illegal status change",
        $"An invoice that is {shownStatus} cannot be {verb}.");

    /// <summary>Only a Draft can be edited (a 409 naming the current status).</summary>
    public static Error NotDraft(string shownStatus) => Error.Conflict(
        "invoice_not_draft",
        "Invoice is locked",
        $"Only a Draft invoice can be edited. This invoice is {shownStatus}.");

    /// <summary>
    /// An invoice that has payments cannot be cancelled (spec 008): there are no refunds, so a Cancelled invoice would be left holding
    /// money with nowhere to say it is owed back.
    /// </summary>
    public static readonly Error HasPayments = Error.Conflict(
        "invoice_has_payments",
        "Invoice has payments",
        "An invoice that has payments cannot be cancelled.");

    /// <summary>A payment can only be recorded on a Sent invoice (also one shown as Overdue): any other status is a 409 naming it.</summary>
    public static Error NotAcceptingPayment(string shownStatus) => Error.Conflict(
        "invoice_payment_not_allowed",
        "Payment not allowed",
        $"An invoice that is {shownStatus} cannot take a payment.");

    /// <summary>The Saudi calendar date of a moment in time (UTC), for comparing with the date a payment carries.</summary>
    private static DateOnly SaudiDate(DateTime utc) =>
        DateOnly.FromDateTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(SaudiOffset).DateTime);

    /// <summary>The status as a person reads it: a Sent invoice past its due date is Overdue.</summary>
    private string ShownStatus(Invoice invoice) => invoice.IsOverdue(Today(time)) ? "Overdue" : invoice.Status.ToString();

    private async Task<Customer> CustomerOfAsync(Invoice invoice, CancellationToken cancellationToken) =>
        await customers.FindByIdAsync(invoice.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("An invoice's customer always exists (foreign key).");

    /// <summary>Today's calendar date in Saudi Arabia, the day a due date is compared with.</summary>
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(SaudiOffset).DateTime);

    public async Task<Result<InvoiceDto>> CreateAsync(InvoiceInput input, CancellationToken cancellationToken)
    {
        var inspection = InvoiceValidator.InspectForCreate(input, Today(time));
        var errors = inspection.Errors;

        var customer = await FindActiveCustomerAsync(inspection.CustomerId, errors, cancellationToken);
        var lineProducts = await FindActiveProductsAsync(inspection.Lines, errors, cancellationToken);

        // Only when everything else is fine: these checks need valid lines and resolved products to mean anything.
        if (errors.Count == 0)
        {
            CheckAmounts(inspection.Discount, inspection.Lines, lineProducts, null, errors);
        }

        if (errors.Count > 0)
        {
            return Result.Fail<InvoiceDto>(Error.Validation(errors));
        }

        // Every check has passed. Who is acting is read now, before anything is made, so a request without a usable user (a defect) throws
        // here and creates nothing.
        var actor = currentUser.Current;
        var now = time.GetUtcNow().UtcDateTime;
        var invoice = Invoice.Create(customer!.Id, inspection.DueDate!.Value, inspection.Notes, now, inspection.Discount);
        foreach (var line in inspection.Lines)
        {
            invoice.AddLine(lineProducts[line.ProductId!.Value]!, line.Quantity, now);
        }

        // The history entry is added to the same unit of work as the invoice: AddAsync writes both, or neither.
        invoices.RecordHistory(InvoiceHistoryBuilder.Created(invoice, customer.Name, actor, now));
        await invoices.AddAsync(invoice, cancellationToken);
        return Result.Success(InvoiceDto.From(invoice, customer, Today(time)));
    }

    /// <summary>
    /// Saves the whole desired Draft: its header and its full list of lines. A line is <b>existing</b> (its quantity may change,
    /// its saved copy is kept), <b>new</b> (copied from the product now) or <b>left out</b> (removed). Everything is checked
    /// first and reported together; only when there is nothing to report is anything changed, so a refused save changes
    /// nothing. A save that changes nothing writes nothing.
    /// </summary>
    public async Task<Result<InvoiceDto>> UpdateAsync(Guid id, InvoiceInput input, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Result.Fail<InvoiceDto>(NotFound);
        }

        // Only a Draft can be edited. This is judged before the body, so a locked invoice is always the same 409 whatever is sent.
        if (!invoice.IsEditable)
        {
            return Result.Fail<InvoiceDto>(NotDraft(ShownStatus(invoice)));
        }

        var inspection = InvoiceValidator.InspectForEdit(input, Today(time), invoice.DueDate);
        var errors = inspection.Errors;

        // Keeping the same customer is always allowed (it may have become inactive since); a different one must be active.
        Customer? customer;
        if (inspection.CustomerId is { } requested && requested != invoice.CustomerId)
        {
            customer = await FindActiveCustomerAsync(requested, errors, cancellationToken);
        }
        else
        {
            customer = await customers.FindByIdAsync(invoice.CustomerId, cancellationToken)
                ?? throw new InvalidOperationException("An invoice's customer always exists (foreign key).");
        }

        // Existing lines are matched by id against THIS invoice's lines.
        var currentLineIds = invoice.Lines.Select(line => line.Id).ToHashSet();
        for (var i = 0; i < inspection.Lines.Count; i++)
        {
            if (inspection.Lines[i].ExistingId is { } lineId && !currentLineIds.Contains(lineId))
            {
                errors[$"lines[{i}].id"] = ["This line does not belong to this invoice."];
            }
        }

        // Only new lines need their product looked up and active; an existing line keeps its saved copy whatever became of the product.
        var lineProducts = await FindActiveProductsAsync(inspection.Lines, errors, cancellationToken);

        if (errors.Count == 0)
        {
            CheckAmounts(inspection.Discount, inspection.Lines, lineProducts, invoice, errors);
        }

        if (errors.Count > 0)
        {
            return Result.Fail<InvoiceDto>(Error.Validation(errors));
        }

        // Nothing is wrong: now apply the changes to the aggregate. Who is acting is read first, so a request without a usable user (a defect)
        // throws before the aggregate is touched. The snapshot of the invoice before the edit needs the customer's name as it is now: the
        // customer already loaded when it is unchanged, the previous one when the edit changes it.
        var actor = currentUser.Current;
        var previousCustomerName = customer!.Id == invoice.CustomerId
            ? customer.Name
            : (await CustomerOfAsync(invoice, cancellationToken)).Name;
        var snapshotBefore = InvoiceHistorySnapshot.From(invoice, previousCustomerName);

        var now = time.GetUtcNow().UtcDateTime;
        var changed = invoice.UpdateDetails(customer!.Id, inspection.DueDate!.Value, inspection.Notes, inspection.Discount, now);

        var keptIds = inspection.Lines.Where(line => line.ExistingId is not null).Select(line => line.ExistingId!.Value).ToHashSet();
        foreach (var lineId in currentLineIds.Where(lineId => !keptIds.Contains(lineId)))
        {
            changed |= invoice.RemoveLine(lineId, now);
        }

        foreach (var line in inspection.Lines)
        {
            changed |= line.ExistingId is { } existingId
                ? invoice.SetLineQuantity(existingId, line.Quantity, now)
                : AddNewLine(invoice, lineProducts[line.ProductId!.Value]!, line.Quantity, now);
        }

        if (changed)
        {
            // What changed is worked out from the two snapshots; the entry is added to the unit of work just before the one save, so it is
            // written with the edit or not at all. An edit that changes nothing writes no entry (and, as before, no save).
            var entry = InvoiceHistoryBuilder.DraftEdited(snapshotBefore, InvoiceHistorySnapshot.From(invoice, customer.Name), actor, now);
            if (entry is not null)
            {
                invoices.RecordHistory(entry);
            }

            if (!await invoices.SaveChangesAsync(cancellationToken))
            {
                return Result.Fail<InvoiceDto>(ChangedByAnotherPerson);
            }
        }

        return Result.Success(InvoiceDto.From(invoice, customer, Today(time)));
    }

    /// <summary>
    /// Draft to Sent (any signed-in staff member). Only a Draft can be sent, and not one whose due date is already past (the person
    /// changes the due date first). Sending records the time and freezes the amounts; nothing else changes. A lost race with another
    /// change to the same invoice is a conflict, never a half-applied send.
    /// </summary>
    public async Task<Result<InvoiceDto>> SendAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Result.Fail<InvoiceDto>(NotFound);
        }

        if (!InvoiceStatusRules.CanChange(invoice.Status, InvoiceStatus.Sent))
        {
            return Result.Fail<InvoiceDto>(IllegalChange(ShownStatus(invoice), "sent"));
        }

        if (invoice.DueDate < Today(time))
        {
            return Result.Fail<InvoiceDto>(Error.Validation(new Dictionary<string, string[]> { ["dueDate"] = [DueDateInThePastToSend] }));
        }

        // Who is acting is read before the aggregate changes, so a request without a usable user (a defect) throws and changes nothing.
        var actor = currentUser.Current;
        var now = time.GetUtcNow().UtcDateTime;

        // The aggregate applies the same rule; if the two ever disagreed this must fail, not report a send that did not happen.
        if (!invoice.Send(now))
        {
            return Result.Fail<InvoiceDto>(IllegalChange(ShownStatus(invoice), "sent"));
        }

        // The entry goes into the same save as the send: both are written, or (on a lost race) neither.
        invoices.RecordHistory(InvoiceHistoryBuilder.Sent(invoice.Id, actor, now));
        if (!await invoices.SaveChangesAsync(cancellationToken))
        {
            return Result.Fail<InvoiceDto>(ChangedByAnotherPerson);
        }

        return Result.Success(InvoiceDto.From(invoice, await CustomerOfAsync(invoice, cancellationToken), Today(time)));
    }

    /// <summary>
    /// Draft or Sent (also Overdue) to Cancelled. <b>Who may cancel (an Admin) is not checked here</b>: the endpoint's policy refuses
    /// anyone else before this runs. Cancelling a Draft freezes its amounts; a Sent invoice keeps the amounts frozen when it was sent.
    /// A Paid or already Cancelled invoice cannot be cancelled (the illegal-change conflict), and neither can an invoice that has a
    /// payment (<see cref="HasPayments"/>, judged after that check): there are no refunds in spec 008.
    /// </summary>
    public async Task<Result<InvoiceDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Result.Fail<InvoiceDto>(NotFound);
        }

        if (!InvoiceStatusRules.CanChange(invoice.Status, InvoiceStatus.Cancelled))
        {
            return Result.Fail<InvoiceDto>(IllegalChange(ShownStatus(invoice), "cancelled"));
        }

        // Judged after the illegal-change check (a Paid invoice is "Paid cannot be cancelled", not "has payments"), before anything changes.
        if (invoice.Payments.Count > 0)
        {
            return Result.Fail<InvoiceDto>(HasPayments);
        }

        // Who is acting is read before the aggregate changes, so a request without a usable user (a defect) throws and changes nothing.
        var actor = currentUser.Current;
        var now = time.GetUtcNow().UtcDateTime;
        var from = invoice.Status;

        if (!invoice.Cancel(now))
        {
            return Result.Fail<InvoiceDto>(IllegalChange(ShownStatus(invoice), "cancelled"));
        }

        // The entry goes into the same save as the cancel, and says the stored status it came from (Overdue is never an event).
        invoices.RecordHistory(InvoiceHistoryBuilder.Cancelled(invoice.Id, from, actor, now));
        if (!await invoices.SaveChangesAsync(cancellationToken))
        {
            return Result.Fail<InvoiceDto>(ChangedByAnotherPerson);
        }

        return Result.Success(InvoiceDto.From(invoice, await CustomerOfAsync(invoice, cancellationToken), Today(time)));
    }

    /// <summary>
    /// Records money received against a Sent invoice (also Overdue), any signed-in staff member (spec 008). The order is fixed
    /// (research R8): the invoice exists (404); it is Sent (else <see cref="NotAcceptingPayment"/>, a 409 naming the status, judged
    /// <b>before</b> the body, as 007 judges a locked edit); the body is valid (every problem reported together, including an amount
    /// above what remains and a date outside the invoice's window); then the payment is added, the invoice becomes Paid in the same
    /// call if nothing remains, and the save is all or nothing. Losing a race with another change to the same invoice is
    /// <see cref="ChangedByAnotherPerson"/>: two payments at the same balance cannot both be saved, so an invoice is never overpaid.
    /// </summary>
    public async Task<Result<InvoiceDto>> RecordPaymentAsync(Guid id, PaymentInput input, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Result.Fail<InvoiceDto>(NotFound);
        }

        if (invoice.Status != InvoiceStatus.Sent)
        {
            return Result.Fail<InvoiceDto>(NotAcceptingPayment(ShownStatus(invoice)));
        }

        DateOnly? sentOn = invoice.SentAtUtc is { } sentAt ? SaudiDate(sentAt) : null;
        var valid = PaymentValidator.Validate(input, Today(time), sentOn, invoice.Remaining);
        if (valid.IsFailure)
        {
            return Result.Fail<InvoiceDto>(valid.Error!);
        }

        // The aggregate applies the same rules and settles the invoice to Paid in this same call; it throws only if the checks above
        // and its own ever disagreed (a bug), never for anything a person can type.
        // Who is acting is read before the aggregate changes, so a request without a usable user (a defect) throws and changes nothing.
        var actor = currentUser.Current;
        var now = time.GetUtcNow().UtcDateTime;
        var paidBefore = invoice.Paid;
        var remainingBefore = invoice.Remaining;

        var payment = valid.Value;
        var recorded = invoice.RecordPayment(payment.PaidOn, payment.Amount, payment.Method, payment.Note, now);

        // One entry for the payment, in the same save; the payment that settles the invoice also shows Sent to Paid in this same entry.
        // The figures are the ones the invoice already holds, read before and after; nothing is calculated here.
        invoices.RecordHistory(InvoiceHistoryBuilder.PaymentRecorded(
            recorded, paidBefore, remainingBefore, invoice.Paid, invoice.Remaining, invoice.Status == InvoiceStatus.Paid, actor, now));
        if (!await invoices.SaveChangesAsync(cancellationToken))
        {
            return Result.Fail<InvoiceDto>(ChangedByAnotherPerson);
        }

        return Result.Success(InvoiceDto.From(invoice, await CustomerOfAsync(invoice, cancellationToken), Today(time)));
    }

    /// <summary>
    /// The Home dashboard's five figures (spec 011), for any signed-in user. A read and nothing else: it never validates, never saves and
    /// never fails on its own (there is nothing to look up by id and nothing a person can get wrong); the figures are read live from the
    /// current invoices and payments, never cached.
    /// </summary>
    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(CancellationToken cancellationToken) =>
        Result.Success(await invoices.GetSummaryAsync(Today(time), cancellationToken));

    /// <summary>
    /// The invoice list, newest first, with paging and (spec 011) an optional narrowing to one of the Home dashboard's views:
    /// <c>status</c> alone, or paired with <c>overdue</c> (only with <c>Sent</c>) or <c>paidMonth</c> (only with <c>Paid</c>). The invoice
    /// list screen itself has no filter control of its own; these exist only so a dashboard card's link can reach its own set.
    /// </summary>
    public async Task<Result<PagedResult<InvoiceSummary>>> SearchAsync(InvoiceQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var page = 1;
        if (!string.IsNullOrWhiteSpace(query.Page)
            && (!int.TryParse(query.Page, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            errors["page"] = ["Page must be a whole number of 1 or more."];
        }

        var pageSize = DefaultPageSize;
        if (!string.IsNullOrWhiteSpace(query.PageSize)
            && (!int.TryParse(query.PageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || pageSize is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Page size must be a whole number from 1 to {MaxPageSize}."];
        }

        InvoiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (TryParseStatus(query.Status, out var parsed))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = ["Status must be Draft, Sent, Paid or Cancelled."];
            }
        }

        if (errors.Count > 0)
        {
            return Result.Fail<PagedResult<InvoiceSummary>>(Error.Validation(errors));
        }

        // overdue/paidMonth matter only paired with their own status; anything else (including no status at all) is silently ignored,
        // never a different error, so a stray value can never change the result.
        var filter = new InvoiceSearchFilter(
            status,
            status == InvoiceStatus.Sent && query.Overdue == "true",
            status == InvoiceStatus.Paid && query.PaidMonth == "true");

        var found = await invoices.SearchAsync(filter, Today(time), page, pageSize, cancellationToken);
        return Result.Success(found.Map(row => row.ToSummary(Today(time))));
    }

    /// <summary>
    /// The prospective check (research R6): works out the amounts the save <b>would</b> produce, before anything is changed, and
    /// reports a line or invoice that is too large (above <see cref="InvoiceMoney.MaxAmount"/>) and a discount above the total net.
    /// An existing line counts at its <b>saved</b> price and VAT with the requested quantity; a new line at its product's current
    /// price and VAT. Nothing here touches the invoice, so a refusal changes nothing.
    /// </summary>
    private static void CheckAmounts(
        decimal discount,
        IReadOnlyList<ValidInvoiceLine> lines,
        IReadOnlyDictionary<Guid, Product?> lineProducts,
        Invoice? invoice,
        Dictionary<string, string[]> errors)
    {
        // In the order the saved invoice will calculate them: kept lines in their saved position order, then new lines in request
        // order (they get the next positions). Ties in the discount sharing depend on the order, so the check must use the same one.
        var kept = lines
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.ExistingId is not null)
            .Select(entry => (entry.index, saved: invoice!.Lines.First(candidate => candidate.Id == entry.line.ExistingId), entry.line))
            .OrderBy(entry => entry.saved.Position).ThenBy(entry => entry.saved.Id)
            .ToList();
        var added = lines
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.ExistingId is null)
            .ToList();

        // Each amount line remembers which request line it came from, so a too-large line is named by its own index.
        var amountLines = new List<AmountLine>(lines.Count);
        var requestIndexes = new List<int>(lines.Count);
        foreach (var entry in kept)
        {
            amountLines.Add(new AmountLine(entry.line.Quantity, entry.saved.UnitPrice, entry.saved.VatPercent));
            requestIndexes.Add(entry.index);
        }

        foreach (var (line, index) in added)
        {
            var product = lineProducts[line.ProductId!.Value]!;
            amountLines.Add(new AmountLine(line.Quantity, product.UnitPrice, product.VatPercent));
            requestIndexes.Add(index);
        }

        // With no discount every amount is at its largest, and the calculator cannot refuse it.
        var undiscounted = InvoiceMoney.Calculate(amountLines, 0m);

        var aLineIsTooLarge = false;
        for (var i = 0; i < undiscounted.Lines.Count; i++)
        {
            if (undiscounted.Lines[i].Net > InvoiceMoney.MaxAmount)
            {
                errors[$"lines[{requestIndexes[i]}].quantity"] = [LineAmountTooLarge];
                aLineIsTooLarge = true;
            }
        }

        var discountFits = discount <= undiscounted.TotalNet;
        var actual = discountFits ? InvoiceMoney.Calculate(amountLines, discount) : undiscounted;
        // A line that is too large already makes the total too large; only that line is named, since fixing it is the fix.
        if (!aLineIsTooLarge && (undiscounted.TotalNet > InvoiceMoney.MaxAmount || actual.GrandTotal > InvoiceMoney.MaxAmount))
        {
            errors["lines"] = [InvoiceTotalTooLarge];
        }

        if (!discountFits)
        {
            errors["discount"] = [DiscountAboveNet];
        }
    }

    private static bool AddNewLine(Invoice invoice, Product product, decimal quantity, DateTime now)
    {
        invoice.AddLine(product, quantity, now);
        return true;
    }

    /// <summary>
    /// One invoice's history, newest first, for any signed-in user (spec 010). <b>Reads only</b>: it never records an entry and never saves, and it
    /// reads the invoice's number by itself (no lines, no payments, no customer). Nothing is recalculated or looked up again: every value is the
    /// text recorded at the time.
    /// </summary>
    public async Task<Result<InvoiceHistoryDto>> GetHistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var number = await invoices.FindNumberAsync(id, cancellationToken);
        if (number is null)
        {
            return Result.Fail<InvoiceHistoryDto>(NotFound);
        }

        var entries = await invoices.ListHistoryAsync(id, cancellationToken);
        return Result.Success(new InvoiceHistoryDto(
            InvoiceNumber.Format(number.Value),
            entries.Any(entry => entry.Kind == HistoryKind.Created),
            entries.Select(HistoryEntryDto.From).ToList()));
    }

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoices.FindByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return Result.Fail<InvoiceDto>(NotFound);
        }

        // The customer's current name and state (not a saved copy). The foreign key guarantees it exists.
        var customer = await customers.FindByIdAsync(invoice.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("An invoice's customer always exists (foreign key).");
        return Result.Success(InvoiceDto.From(invoice, customer, Today(time)));
    }

    /// <summary>Looks the customer up when its id was readable; a missing or inactive one is a <c>customerId</c> error (the answer is the same for both).</summary>
    private async Task<Customer?> FindActiveCustomerAsync(Guid? customerId, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        if (customerId is not { } id)
        {
            return null;
        }

        var customer = await customers.FindByIdAsync(id, cancellationToken);
        if (customer is null || !customer.IsActive)
        {
            errors["customerId"] = [ChooseActiveCustomer];
            return null;
        }

        return customer;
    }

    /// <summary>
    /// Looks up the product of every <b>new</b> line (existing lines keep their saved copy and need no product). Each distinct
    /// product is read once; a missing or inactive one is an error on every line that uses it.
    /// </summary>
    private async Task<Dictionary<Guid, Product?>> FindActiveProductsAsync(
        IReadOnlyList<ValidInvoiceLine> lines,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, Product?>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].ProductId is not { } productId)
            {
                continue;
            }

            if (!found.TryGetValue(productId, out var product))
            {
                product = await products.FindByIdAsync(productId, cancellationToken);
                found[productId] = product;
            }

            if (product is null || !product.IsActive)
            {
                errors[$"lines[{i}].productId"] = [ChooseActiveProduct];
            }
        }

        return found;
    }

    /// <summary>Only the four names are accepted (exact case), matching every other stored status in this codebase; a numeric string such
    /// as "1" is not (`Enum.TryParse` alone would silently accept it as `Sent`, since it also parses an enum's underlying number).</summary>
    private static bool TryParseStatus(string value, out InvoiceStatus status)
    {
        foreach (var candidate in Enum.GetValues<InvoiceStatus>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.Ordinal))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
