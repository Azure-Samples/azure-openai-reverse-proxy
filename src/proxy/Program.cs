using Proxy.Customizations;
using Proxy.ServiceDiscovery;
using Proxy.ServiceDiscovery.RouteUpdates;
using Proxy.Telemetry;
using Proxy.Transformers;
using Yarp.ReverseProxy.Health;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IReverseProxyBuilder proxyBuilder = builder.Services.AddReverseProxy();

proxyBuilder.AddTransforms(AbsoluteUriResponseTransformer.Transform);

IConfigurationSection modelDeploymentsDiscovery = builder.Configuration.GetSection("ModelDeploymentsDiscovery");

if (modelDeploymentsDiscovery.Exists())
{
    _ = builder.Services.AddSingleton<RouteUpdateChannelProvider>();
    _ = builder.Services.AddHostedService<RouteUpdateWorker>();
    _ = builder.Services.AddHostedService<AzureOpenAIModelDeploymentsDiscoveryWorker>()
      .Configure<AzureOpenAIModelDeploymentsDiscoveryWorkerOptions>(modelDeploymentsDiscovery);

    _ = proxyBuilder.LoadFromMemory([], []);
}
else
{
    _ = proxyBuilder.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
}

builder.Services.AddSingleton<IPassiveHealthCheckPolicy, AzureOpenAIPassiveHealthCheckPolicy>();
builder.Services.AddHttpClient(AzureOpenAIPassiveHealthCheckPolicy.HttpClientName);

builder.AddOpenTelemetryBuilder();
// Export metrics from all HTTP clients registered in services


WebApplication app = builder.Build();

app.MapReverseProxy();
app.UseCustomMetrics();
app.Run();
