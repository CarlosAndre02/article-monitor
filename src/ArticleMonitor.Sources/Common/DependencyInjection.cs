using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Sources.Common.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArticleMonitor.Sources.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddSourcesCommon(this IServiceCollection services)
    {
        services.TryAddSingleton<IFallbackContentExtractor, ReadabilityExtractor>();
        return services;
    }
}
