using BinStash.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinStash.Infrastructure.Data.Configurations;

public class ReleaseMetricsEntityTypeConfiguration : IEntityTypeConfiguration<ReleaseMetrics>
{
    public void Configure(EntityTypeBuilder<ReleaseMetrics> builder)
    {
        builder.ToTable("ReleaseMetrics");
        
        builder.HasKey(rm => rm.ReleaseId);
        
        builder.Property(rm => rm.ReleaseId).ValueGeneratedNever();
        builder.Property(rm => rm.IngestSessionId).IsRequired();
        builder.Property(rm => rm.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        
        builder.HasIndex(rm => rm.CreatedAt);

        builder.HasOne(rm => rm.IngestSession)
            .WithOne()
            .HasForeignKey<ReleaseMetrics>(rm => rm.IngestSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}