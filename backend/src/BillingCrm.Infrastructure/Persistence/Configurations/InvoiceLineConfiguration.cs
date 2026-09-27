using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines", table =>
        {
            table.HasCheckConstraint("CK_InvoiceLines_Unit", "[Unit] IN ('Hour', 'Item', 'Service')");
            table.HasCheckConstraint("CK_InvoiceLines_UnitPrice", "[UnitPrice] >= 0");
            table.HasCheckConstraint("CK_InvoiceLines_VatPercent", "[VatPercent] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_InvoiceLines_Quantity", "[Quantity] > 0 AND [Quantity] <= 999999.99");
        });

        builder.HasKey(line => line.Id);

        // The application makes the key (Guid.NewGuid) and the database never does. This is what makes EF insert a line that
        // is added to a tracked invoice, instead of mistaking a line with a key for one that already exists.
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Position).IsRequired();

        // A reference only. Products are never deleted, and nothing joins to the live product to show a line.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // The snapshot: written once when the line is created and never rewritten.
        builder.Property(line => line.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(line => line.ProductSku).HasMaxLength(50).IsRequired();
        builder.Property(line => line.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Money: SAR, exact decimal.
        builder.Property(line => line.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();

        // A percentage (0 to 100), not money.
        builder.Property(line => line.VatPercent).HasColumnType("decimal(5,2)").IsRequired();

        // A quantity, not money: up to 999,999.99, so decimal(9,2) holds it exactly.
        builder.Property(line => line.Quantity).HasColumnType("decimal(9,2)").IsRequired();

        // The line's amounts as they were when its invoice left Draft (null while it is a Draft).
        builder.Property(line => line.FrozenNet).HasColumnType("decimal(18,2)");
        builder.Property(line => line.FrozenDiscountShare).HasColumnType("decimal(18,2)");
        builder.Property(line => line.FrozenVat).HasColumnType("decimal(18,2)");

        // Not unique: removing the last line and adding a new one in the same save could otherwise collide.
        builder.HasIndex(line => new { line.InvoiceId, line.Position }).HasDatabaseName("IX_InvoiceLines_InvoiceId_Position");
    }
}
