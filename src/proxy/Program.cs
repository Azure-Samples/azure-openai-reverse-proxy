using Prometheus;
using Proxy.Policies.HealthChecks;
using Yarp.ReverseProxy.Health;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddSingleton<IPassiveHealthCheckPolicy, TooManyRequestsPolicy>();
builder.Services.AddSingleton<IPassiveHealthCheckPolicy, RateLimitPolicy>();

// Define an HTTP client that reports metrics about its usage
builder.Services.AddHttpClient(RateLimitPolicy.HttpClientName);

// Export metrics from all HTTP clients registered in services
builder.Services.UseHttpClientMetrics();

WebApplication app = builder.Build();

app.MapReverseProxy();

// Add middleware to handle scraping requests for metrics
app.UseHttpMetrics();

// Enable the /metrics page to export Prometheus metrics
app.MapMetrics();

app.Run();
