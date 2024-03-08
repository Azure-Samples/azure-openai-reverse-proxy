using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Prometheus;
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

// Define an HTTP client that reports metrics about its usage
builder.Services.AddHttpClient(AzureOpenAIPassiveHealthCheckPolicy.HttpClientName);

OpenTelemetryBuilder openTelemetry = builder.Services.AddOpenTelemetry();

// Add Metrics for ASP.NET Core and our custom metrics and export to Prometheus
openTelemetry.WithMetrics(metrics => metrics
    .AddMeter("ReverseProxy")
    .AddPrometheusExporter());

// Configure OpenTelemetry Resources with the application name
openTelemetry.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName));

// Export metrics from all HTTP clients registered in services
builder.Services.UseHttpClientMetrics();

builder.Services.AddOpenTelemetryServices();

WebApplication app = builder.Build();

app.MapReverseProxy();

app.UsePrometheusMetrics();

app.Run();
