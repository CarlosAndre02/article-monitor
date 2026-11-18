using ArticleMonitor.Application.Collection;
using ArticleMonitor.Application.Extraction;
using ArticleMonitor.Application.Sources;
using ArticleMonitor.Domain.Collection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ArticleMonitor.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<CollectionOptions>()
            .BindConfiguration(CollectionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ScheduleOptions>()
            .BindConfiguration(ScheduleOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                schedule => schedule.TryCreateSchedule(out _),
                "Collector:Schedule needs valid HH:mm times and time zone.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(CreateSchedule);
        services.AddSingleton<SourceModuleCatalog>();
        services.AddSingleton<ICollectionCycle, CollectionCycle>();

        services.AddScoped<CollectSource>();
        services.AddScoped<SourceHarvester>();
        services.AddScoped<ArticleIngestor>();
        services.AddScoped<ArticleContentReader>();

        return services;
    }

    private static CollectionSchedule CreateSchedule(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<ScheduleOptions>>().Value;
        return options.TryCreateSchedule(out var schedule)
            ? schedule
            : throw new InvalidOperationException("Collector:Schedule is invalid.");
    }
}
