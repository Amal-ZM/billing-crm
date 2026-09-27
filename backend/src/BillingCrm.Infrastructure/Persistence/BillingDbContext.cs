using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Persistence;

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Permanent, append-only (spec 010): the database refuses to change or remove a row.</summary>
    public DbSet<InvoiceHistoryEntry> InvoiceHistoryEntries => Set<InvoiceHistoryEntry>();

    /// <summary>The name of the sequence that hands out invoice numbers (no gaps guaranteed, no duplicates possible).</summary>
    public const string InvoiceNumberSequence = "InvoiceNumbers";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // One number per invoice, drawn atomically by the database: unique under simultaneous creation without any retry
        // logic. Stops at 999,999 (the six-digit form); a later migration can raise it.
        modelBuilder.HasSequence<int>(InvoiceNumberSequence)
            .StartsAt(1)
            .IncrementsBy(1)
            .HasMin(1)
            .HasMax(InvoiceNumber.Max);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }
}
