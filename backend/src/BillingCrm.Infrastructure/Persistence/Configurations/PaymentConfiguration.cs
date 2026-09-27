using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            // The database holds what it can on its own: a positive amount within the money ceiling (InvoiceMoney.MaxAmount) and a known
            // method. "The payments add up to no more than the grand total" needs the other rows, so that is the service's rule, kept
            // safe under simultaneous payments by the invoice's row version.
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0 AND [Amount] <= 9999999999999.99");
            table.HasCheckConstraint("CK_Payments_Method", "[Method] IN ('Cash','Bank','Other')");

            // Payments are permanent (spec 008), enforced below the application layer the same way spec 010 enforces it for invoice
            // history (migration AddPaymentsImmutability): telling EF about the trigger here just means EF's own INSERT is unaffected.
            table.HasTrigger("TR_Payments_Immutable");
        });

        builder.HasKey(payment => payment.Id);

        // The application makes the key (Guid.NewGuid) and the database never does, so EF inserts a payment added to a tracked invoice.
        builder.Property(payment => payment.Id).ValueGeneratedNever();

        // A calendar date with no time of day (the day the money was received).
        builder.Property(payment => payment.PaidOn).HasColumnType("date").IsRequired();

        // Money: SAR, exact decimal.
        builder.Property(payment => payment.Amount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(10).IsRequired();

        // nvarchar: a note may be Arabic, English, or a mix, and may contain line breaks.
        builder.Property(payment => payment.Note).HasMaxLength(1000);

        // datetime2 has no time zone, so read values are tagged as UTC again (they are always written as UTC).
        builder.Property(payment => payment.RecordedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // Serves loading an invoice's payments in order.
        builder.HasIndex(payment => new { payment.InvoiceId, payment.RecordedAtUtc }).HasDatabaseName("IX_Payments_InvoiceId_RecordedAtUtc");
    }
}
