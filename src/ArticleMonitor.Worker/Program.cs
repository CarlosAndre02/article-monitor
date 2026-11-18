using ArticleMonitor.Application;
using ArticleMonitor.Infrastructure;
using ArticleMonitor.Sources.Common;
using ArticleMonitor.Sources.G1;
using ArticleMonitor.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddSourcesCommon()
    .AddG1Source();

builder.Services.AddHostedService<CollectionScheduler>();

await builder.Build().RunAsync();
