using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", table =>
        {
            // Overdue is deliberately not here: it is derived from a Sent invoice's due date and never stored.
            table.HasCheckConstraint("CK_Invoices_Status", "[Status] IN ('Draft','Sent','Paid','Cancelled')");

            // The times must agree with the status: a Draft has neither, a Sent or Paid invoice has been sent and not cancelled, a
            // Cancelled invoice has a cancelled time (and a sent time only if it was sent first).
            table.HasCheckConstraint(
                "CK_Invoices_StatusTimes",
                "([Status] = 'Draft' AND [SentAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) " +
                "OR ([Status] IN ('Sent','Paid') AND [SentAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) " +
                "OR ([Status] = 'Cancelled' AND [CancelledAtUtc] IS NOT NULL)");

            // The frozen amounts exist exactly when the invoice has left Draft.
            table.HasCheckConstraint(
                "CK_Invoices_FrozenAmounts",
                "([Status] = 'Draft' AND [FrozenTotalNet] IS NULL AND [FrozenNetAfterDiscount] IS NULL AND [FrozenTotalVat] IS NULL AND [FrozenGrandTotal] IS NULL) " +
                "OR ([Status] <> 'Draft' AND [FrozenTotalNet] IS NOT NULL AND [FrozenNetAfterDiscount] IS NOT NULL AND [FrozenTotalVat] IS NOT NULL AND [FrozenGrandTotal] IS NOT NULL)");

            // "Not more than the total net" needs the other rows, so that part is a service rule; the sign is enough for a check.
            table.HasCheckConstraint("CK_Invoices_Discount", "[Discount] >= 0");
        });

        builder.HasKey(invoice => invoice.Id);

        // The database draws the number from the sequence when the row is inserted; the entity leaves it at 0.
        builder.Property(invoice => invoice.Number)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql($"NEXT VALUE FOR [{BillingDbContext.InvoiceNumberSequence}]");

        // Customers are never deleted (they are retired), so restrict is enough and protects the history.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(invoice => invoice.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // A calendar date with no time of day.
        builder.Property(invoice => invoice.DueDate).HasColumnType("date").IsRequired();

        // nvarchar: notes may be Arabic, English, or a mix, and may contain line breaks.
        builder.Property(invoice => invoice.Notes).HasMaxLength(1000);

        // The header discount in SAR: the only amount stored on an invoice (everything else is calculated from the lines). The default of
        // 0 is what every invoice made before spec 006 gets when the column is added, so they need no backfill.
        builder.Property(invoice => invoice.Discount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        // datetime2 has no time zone, so read values are tagged as UTC again (they are always written as UTC).
        builder.Property(invoice => invoice.CreatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(invoice => invoice.UpdatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // When it was sent and when it was cancelled (each null until it happens), tagged as UTC like the times above.
        builder.Property(invoice => invoice.SentAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

        builder.Property(invoice => invoice.CancelledAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

        // The amounts as they were when the invoice left Draft (null while it is a Draft); see CK_Invoices_FrozenAmounts.
        builder.Property(invoice => invoice.FrozenTotalNet).HasColumnType("decimal(18,2)");
        builder.Property(invoice => invoice.FrozenNetAfterDiscount).HasColumnType("decimal(18,2)");
        builder.Property(invoice => invoice.FrozenTotalVat).HasColumnType("decimal(18,2)");
        builder.Property(invoice => invoice.FrozenGrandTotal).HasColumnType("decimal(18,2)");

        // Optimistic concurrency: every save of an invoice or its lines writes this row, and a save made from a stale copy is refused
        // (DbUpdateConcurrencyException, which the store reports as "changed by someone else").
        builder.Property(invoice => invoice.RowVersion).IsRowVersion();

        // The lines belong to the invoice and are read and written through it (field access: the property is a read-only view).
        builder.HasMany(invoice => invoice.Lines)
            .WithOne()
            .HasForeignKey(line => line.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(invoice => invoice.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Payments are read and written through the invoice too. No cascade: invoices are never deleted, and a payment (money on
        // record) must never vanish silently, so removing an invoice that has payments would be refused by the database.
        builder.HasMany(invoice => invoice.Payments)
            .WithOne()
            .HasForeignKey(payment => payment.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(invoice => invoice.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(invoice => invoice.Number).IsUnique().HasDatabaseName("IX_Invoices_Number");
        builder.HasIndex(invoice => invoice.CustomerId).HasDatabaseName("IX_Invoices_CustomerId");

        // Serves the list: newest first.
        builder.HasIndex(invoice => new { invoice.CreatedAtUtc, invoice.Number })
            .HasDatabaseName("IX_Invoices_CreatedAtUtc_Number");
    }
}
