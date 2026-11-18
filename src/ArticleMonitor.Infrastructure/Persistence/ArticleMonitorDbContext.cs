using ArticleMonitor.Application.Persistence;
using ArticleMonitor.Domain.Articles;
using ArticleMonitor.Domain.Collection;
using ArticleMonitor.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace ArticleMonitor.Infrastructure.Persistence;

public sealed class ArticleMonitorDbContext(DbContextOptions<ArticleMonitorDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArticleMonitorDbContext).Assembly);
}
