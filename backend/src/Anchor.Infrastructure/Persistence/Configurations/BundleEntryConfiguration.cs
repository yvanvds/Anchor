using Anchor.Domain.Bundles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anchor.Infrastructure.Persistence.Configurations;

internal sealed class BundleEntryConfiguration : IEntityTypeConfiguration<BundleEntry>
{
    public void Configure(EntityTypeBuilder<BundleEntry> builder)
    {
        builder.ToTable("BundleEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.MatchType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.Value).IsRequired().HasMaxLength(512);

        builder.HasOne(e => e.Bundle)
            .WithMany(b => b.Entries)
            .HasForeignKey(e => e.BundleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.BundleId);
    }
}
