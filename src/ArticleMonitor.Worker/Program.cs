using ArticleMonitor.Application;
using ArticleMonitor.Infrastructure;
using ArticleMonitor.Sources.Common;
using ArticleMonitor.Sources.G1;
using ArticleMonitor.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddTelemetry();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddSourcesCommon()
    .AddG1Source();

if (ArticlesCommand.IsInvoked(args))
{
    using var host = builder.Build();
    return await ArticlesCommand.RunAsync(host.Services, args, Console.Out, Console.Error);
}

builder.Services.AddHostedService<CollectionScheduler>();

await builder.Build().RunAsync();
return 0;
