using System.ComponentModel.DataAnnotations;

namespace ArticleMonitor.Infrastructure.Http;

public sealed class CollectorHttpOptions
{
    public const string SectionName = "Collector:Http";

    /// <summary>
    /// Identifiable User-Agent, "product/version (comment)". The product part is the token matched in robots.txt.
    /// </summary>
    [Required]
    [RegularExpression(@"^[A-Za-z0-9._-]+/\S+.*$", ErrorMessage = "UserAgent must start with product/version.")]
    public string UserAgent { get; set; } = string.Empty;

    [Range(0.05, 50)]
    public double RequestsPerSecondPerHost { get; set; } = 1;

    [Range(1024, 50L * 1024 * 1024)]
    public long MaxResponseBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>
    /// Limit for article pages, below <see cref="MaxResponseBytes"/>: parsing pathological HTML costs time that grows
    /// faster than the page size, and extraction cannot be cancelled.
    /// </summary>
    [Range(1024, 50L * 1024 * 1024)]
    public long MaxArticlePageBytes { get; set; } = 2L * 1024 * 1024;

    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan RobotsTxtCacheDuration { get; set; } = TimeSpan.FromDays(1);

    public string ProductToken => UserAgent.Split('/', 2)[0];
}
