using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArticleMonitor.Infrastructure.Persistence.Configurations;

internal sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    private const int StatusMaxLength = 16;

    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("Articles");
        builder.HasKey(article => article.Id);

        builder.Property(article => article.Url).HasMaxLength(CanonicalUrl.MaxLength).IsRequired();
        builder.Property(article => article.UrlHash)
            .HasConversion(ValueMappings.UrlHashConverter, ValueMappings.UrlHashComparer)
            .HasColumnType($"binary({UrlHash.Length})")
            .IsRequired();
        builder.HasIndex(article => article.UrlHash).IsUnique();

        builder.Property(article => article.Title).HasMaxLength(ArticleHeadline.TitleMaxLength).IsRequired();
        builder.Property(article => article.Lead).HasMaxLength(ArticleHeadline.LeadMaxLength);
        builder.Property(article => article.Language).HasMaxLength(Article.LanguageMaxLength).IsRequired();
        builder.Property(article => article.Content)
            .HasConversion((ValueConverter)new BrotliTextConverter())
            .HasColumnType("varbinary(max)");
        builder.Property(article => article.ContentStatus).HasConversion<string>().HasMaxLength(StatusMaxLength);

        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(article => article.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(article => new { article.SourceId, article.PublishedAt });
    }
}
