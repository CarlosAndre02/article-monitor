using ArticleMonitor.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace ArticleMonitor.Sources.G1;

public static class DependencyInjection
{
    public static IServiceCollection AddG1Source(this IServiceCollection services)
    {
        services.AddSingleton<ISourceModule, G1SourceModule>();
        return services;
    }
}
