using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArticleMonitor.Infrastructure.Persistence.Configurations;

internal sealed class CollectionRunConfiguration : IEntityTypeConfiguration<CollectionRun>
{
    private const int EnumMaxLength = 32;

    public void Configure(EntityTypeBuilder<CollectionRun> builder)
    {
        builder.ToTable("CollectionRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.Trigger).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.StopReason).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.Errors).HasMaxLength(CollectionRun.ErrorsMaxLength);

        builder.HasOne<Source>().WithMany().HasForeignKey(run => run.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.SourceId, run.StartedAt });
        builder.HasIndex(run => run.Status);
    }
}
