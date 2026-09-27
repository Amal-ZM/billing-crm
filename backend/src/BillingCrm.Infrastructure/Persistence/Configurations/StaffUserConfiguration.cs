using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        // Exactly two roles may ever be stored; the domain enum and this constraint agree.
        builder.ToTable("StaffUsers", table =>
            table.HasCheckConstraint("CK_StaffUsers_Role", "[Role] IN ('Admin', 'Agent')"));

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();

        builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();

        builder.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();

        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_StaffUsers_NormalizedEmail");
    }
}
