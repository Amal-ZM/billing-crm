using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using BillingCrm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingCrm.Infrastructure.Persistence.Configurations;

public class InvoiceHistoryEntryConfiguration : IEntityTypeConfiguration<InvoiceHistoryEntry>
{
    public const string ImmutableTrigger = "TR_InvoiceHistoryEntries_Immutable";

    // Arabic stays readable in the stored text instead of being escaped; nulls are kept (a null "before" is meaningful).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Configure(EntityTypeBuilder<InvoiceHistoryEntry> builder)
    {
        builder.ToTable("InvoiceHistoryEntries", table =>
        {
            // The database holds what it can on its own: only the five kinds, only the two roles, and changes that are valid JSON.
            // The trigger below is what makes an entry permanent: no update and no delete ever succeeds (the migration creates it).
            table.HasCheckConstraint("CK_InvoiceHistoryEntries_Kind", "[Kind] IN ('Created','DraftEdited','Sent','Payment','Cancelled')");
            table.HasCheckConstraint("CK_InvoiceHistoryEntries_ActorRole", "[ActorRole] IN ('Admin','Agent')");
            table.HasCheckConstraint("CK_InvoiceHistoryEntries_ChangesJson", "ISJSON([ChangesJson]) = 1");
            table.HasTrigger(ImmutableTrigger);
        });

        builder.HasKey(entry => entry.Id);

        // The application makes the key (Guid.NewGuid) and the database never does.
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        // The order in which entries were applied: the database draws it, so it is total and never repeats. Not the key.
        builder.Property(entry => entry.Sequence).UseIdentityColumn().ValueGeneratedOnAdd();
        builder.HasIndex(entry => entry.Sequence).IsUnique().HasDatabaseName("IX_InvoiceHistoryEntries_Sequence");

        // History is never cascaded: it must outlive anything done to the invoice (nothing deletes invoices anyway).
        builder.HasOne<Invoice>().WithMany().HasForeignKey(entry => entry.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        // Serves "this invoice's history, newest first".
        builder.HasIndex(entry => new { entry.InvoiceId, entry.Sequence }).HasDatabaseName("IX_InvoiceHistoryEntries_InvoiceId_Sequence");

        builder.Property(entry => entry.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.ActorRole).HasConversion<string>().HasMaxLength(20).IsRequired();

        // The user's email as it was then, kept as text (no link to the account, so a later change to the account cannot alter it).
        builder.Property(entry => entry.ActorEmail).HasMaxLength(256).IsRequired();

        // datetime2 has no time zone, so read values are tagged as UTC again (they are always written as UTC).
        builder.Property(entry => entry.OccurredAtUtc)
            .HasColumnType("datetime2")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // What changed: one text column holding [{field, before, after}], every value text as it was at the time.
        var converter = new ValueConverter<IReadOnlyList<HistoryChange>, string>(
            changes => JsonSerializer.Serialize(changes, Json),
            text => new ReadOnlyCollection<HistoryChange>(JsonSerializer.Deserialize<List<HistoryChange>>(text, Json)!));
        var comparer = new ValueComparer<IReadOnlyList<HistoryChange>>(
            (left, right) => left!.SequenceEqual(right!),
            changes => changes.Aggregate(0, (hash, change) => HashCode.Combine(hash, change)),
            changes => new ReadOnlyCollection<HistoryChange>(changes.ToList()));
        builder.Property(entry => entry.Changes)
            .HasColumnName("ChangesJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(converter, comparer)
            .IsRequired();
    }
}
