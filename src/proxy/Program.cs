using Prometheus;
using Proxy.Customizations;
using Proxy.Telemetry;
using Proxy.Transformers;
using Yarp.ReverseProxy.Health;
using Proxy.ServiceDiscovery.RouteUpdates;
using Proxy.ServiceDiscovery;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IReverseProxyBuilder proxyBuilder = builder.Services.AddReverseProxy();

proxyBuilder.AddTransforms(AbsoluteUriResponseTransformer.Transform);

IConfigurationSection modelDeploymentsDiscovery = builder.Configuration.GetSection("ModelDeploymentsDiscovery");

if (modelDeploymentsDiscovery != null)
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

// Define an HTTP client that reports metrics about its usage
builder.Services.AddHttpClient(AzureOpenAIPassiveHealthCheckPolicy.HttpClientName);

// Export metrics from all HTTP clients registered in services
builder.Services.UseHttpClientMetrics();

builder.Services.AddPrometheusServices();

WebApplication app = builder.Build();

app.MapReverseProxy();

app.UsePrometheusMetrics();

app.Run();
