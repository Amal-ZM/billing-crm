using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_Unit", "[Unit] IN ('Hour', 'Item', 'Service')");
            // The upper bound (spec 012, closing a 004 deferred item) matches ProductValidator.MaxUnitPrice exactly: the database
            // enforces the same ceiling the application layer already does, not a new business rule.
            table.HasCheckConstraint("CK_Products_UnitPrice", "[UnitPrice] >= 0 AND [UnitPrice] <= 999999999.99");
            table.HasCheckConstraint("CK_Products_VatPercent", "[VatPercent] BETWEEN 0 AND 100");
        });

        builder.HasKey(product => product.Id);

        // nvarchar: names may be Arabic, English, or a mix.
        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();

        // Stored already upper-case (the validator normalizes), so uniqueness does not depend on collation.
        builder.Property(product => product.Sku).HasMaxLength(50).IsRequired();

        builder.Property(product => product.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Money: SAR, exact decimal.
        builder.Property(product => product.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();

        // A percentage (0 to 100), not money: decimal(5,2) holds 0.00 to 100.00 exactly.
        builder.Property(product => product.VatPercent).HasColumnType("decimal(5,2)").IsRequired();

        builder.Property(product => product.IsActive).IsRequired();

        // datetime2 has no time zone, so read values are tagged as UTC again (they are always written as UTC).
        builder.Property(product => product.CreatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(product => product.UpdatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // The real guarantee behind "one product per SKU", even for two saves at the same instant.
        builder.HasIndex(product => product.Sku)
            .IsUnique()
            .HasDatabaseName("IX_Products_Sku");

        builder.HasIndex(product => product.Name).HasDatabaseName("IX_Products_Name");
    }
}
