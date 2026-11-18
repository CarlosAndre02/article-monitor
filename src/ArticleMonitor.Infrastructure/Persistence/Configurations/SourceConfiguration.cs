using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArticleMonitor.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Sources");
        builder.HasKey(source => source.Id);

        builder.Property(source => source.Key)
            .HasConversion(ValueMappings.SourceKeyConverter)
            .HasMaxLength(SourceKey.MaxLength)
            .IsRequired();
        builder.HasIndex(source => source.Key).IsUnique();

        builder.Property(source => source.DiscoveryState).HasColumnType("nvarchar(max)");
    }
}
