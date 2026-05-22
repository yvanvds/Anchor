using Anchor.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anchor.Infrastructure.Persistence.Configurations;

internal sealed class SessionBundleConfiguration : IEntityTypeConfiguration<SessionBundle>
{
    public void Configure(EntityTypeBuilder<SessionBundle> builder)
    {
        builder.ToTable("SessionBundles");
        builder.HasKey(sb => new { sb.SessionId, sb.BundleId });

        builder.HasOne(sb => sb.Session)
            .WithMany(s => s.SessionBundles)
            .HasForeignKey(sb => sb.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sb => sb.Bundle)
            .WithMany()
            .HasForeignKey(sb => sb.BundleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(sb => sb.BundleId);
    }
}
