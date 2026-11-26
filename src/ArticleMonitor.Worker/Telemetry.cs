using OpenTelemetry;
using OpenTelemetry.Resources;

namespace ArticleMonitor.Worker;

internal static class Telemetry
{
    private const string ServiceName = "article-monitor";

    // Standard OpenTelemetry setting; the exporter reads it and the other OTEL_EXPORTER_OTLP_* from configuration.
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Sends the logs, with their structured fields and scopes, to an OTLP collector when one is configured.
    /// Without it, logs go to the console only.
    /// </summary>
    public static IHostApplicationBuilder AddTelemetry(this IHostApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey])) return builder;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithLogging(configureBuilder: null, configureOptions: options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            })
            .UseOtlpExporter();

        return builder;
    }
}
