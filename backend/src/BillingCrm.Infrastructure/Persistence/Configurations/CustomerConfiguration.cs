using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", table =>
            table.HasCheckConstraint("CK_Customers_Type", "[Type] IN ('Company', 'Individual')"));

        builder.HasKey(customer => customer.Id);

        // nvarchar throughout: names and addresses may be Arabic, English, or a mix.
        builder.Property(customer => customer.Name).HasMaxLength(200).IsRequired();

        builder.Property(customer => customer.Type).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(customer => customer.Email).HasMaxLength(256).IsRequired();

        builder.Property(customer => customer.NormalizedEmail).HasMaxLength(256).IsRequired();

        builder.Property(customer => customer.Phone).HasMaxLength(20).IsRequired();

        builder.Property(customer => customer.VatNumber).HasMaxLength(30);

        builder.Property(customer => customer.Address).HasMaxLength(500).IsRequired();

        builder.Property(customer => customer.IsActive).IsRequired();

        // datetime2 has no time zone, so read values are tagged as UTC again (they are always written as UTC).
        builder.Property(customer => customer.CreatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(customer => customer.UpdatedAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // The real guarantee behind "one customer per email", even for two saves at the same instant.
        builder.HasIndex(customer => customer.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_Customers_NormalizedEmail");

        builder.HasIndex(customer => customer.Name).HasDatabaseName("IX_Customers_Name");
    }
}
