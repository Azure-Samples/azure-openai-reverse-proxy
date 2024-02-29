using Prometheus;
using Proxy.Customizations;
using Proxy.Telemetry;
using Proxy.Transformers;
using Yarp.ReverseProxy.Health;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
                .AddTransforms(AbsoluteUriResponseTransformer.Transform)
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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
