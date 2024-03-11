using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Caching.Memory;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Prometheus;
using Proxy.OpenAI;
using Proxy.Transformers;

namespace Proxy.Telemetry
{
    public class AzureOpenAICustomMetricsPublisherMiddleware(RequestDelegate next)
    {
        private static readonly Meter meter = new("ReverseProxy");

        private static readonly Counter<int> FailedHttpRequestsCounter = meter.CreateCounter<int>("azure_openai_failed_http_requests");

        private readonly RequestDelegate _next = next;

        public async Task InvokeAsync(HttpContext context, IMemoryCache cache)
        {
            await _next(context);

            if (context.Response.Headers.TryGetValue(AbsoluteUriResponseTransformer.AbsoluteUriHeaderKey, out var values))
            {
                string destinationAddress = values.ToString();

                (string accountName, string deploymentName) = GetResourceDetailsFromDestination(cache, destinationAddress);

                if (context.Response.StatusCode is >= 400 and <= 599)
                {
                    FailedHttpRequestsCounter.Add(1, new("account_name", accountName), new("deployment_name", deploymentName), new("status_code", context.Response.StatusCode));
                }
                else
                {
                    (int remainingRequests, int remainingTokens) = OpenAIRemainingCapacityParser
                        .GetAzureOpenAIRemainingCapacity(context.Response);

                    _ = meter.CreateObservableGauge<int>(
                        "azure_openai_remaining_requests",
                        () => new(remainingRequests, new("account_name", accountName), new("deployment_name", deploymentName)));

                    _ = meter.CreateObservableGauge<int>(
                        "azure_openai_remaining_tokens",
                        () => new(remainingTokens, new("account_name", accountName), new("deployment_name", deploymentName)));
                }
            }
        }

        private static ResourceDetails GetResourceDetailsFromDestination(IMemoryCache cache, string destinationAddress)
        {
            if (cache.TryGetValue(destinationAddress, out ResourceDetails resourceDetails))
                return resourceDetails;

            Uri uri = new(destinationAddress);

            string accountName = uri.Host.Split('.')[0].Replace('-', '_');

            string[] segments = uri.Segments;
            int deploymentIndex = Array.IndexOf(segments, "deployments/");

            if (deploymentIndex == -1 || deploymentIndex >= segments.Length - 1)
            {
                throw new UriFormatException("Deployment name not found in the destination URL.");
            }

            string deploymentName = segments[deploymentIndex + 1].TrimEnd('/').Replace('-', '_');

            return cache.Set(destinationAddress, new ResourceDetails()
            {
                AccountName = accountName,
                DeploymentName = deploymentName
            }, TimeSpan.FromHours(1)); // 1h to avoid memory leaks, in case a single proxy instance keeps refreshing its destination list
        }

        private readonly struct ResourceDetails
        {
            public string AccountName { get; init; }
            public string DeploymentName { get; init; }

            public void Deconstruct(out string accountName, out string deploymentName)
            {
                accountName = AccountName;
                deploymentName = DeploymentName;
            }
        }
    }

    public static class MetricsPublisherMiddlewareExtensions
    {
        public static WebApplicationBuilder AddOpenTelemetryBuilder(this WebApplicationBuilder builder)
        {
            OpenTelemetryBuilder openTelemetryBuilder = builder.Services.AddOpenTelemetry();

            IConfigurationSection appInsightsSection = builder.Configuration.GetSection("ApplicationInsights");

            if (appInsightsSection.Exists())
            {
                _ = openTelemetryBuilder.UseAzureMonitor(options =>
                    {
                        options.ConnectionString = appInsightsSection["ConnectionString"];
                    });
            }

            // TODO: Prometheus validation
            _ = openTelemetryBuilder.WithMetrics(metrics => ConfigureMeter(metrics));

            // Configure OpenTelemetry Resources with the application name
            _ = openTelemetryBuilder.ConfigureResource(resource => resource
                .AddService(serviceName: builder.Environment.ApplicationName));

            _ = builder.Services.AddOpenTelemetryServices();


            // Expose /metrics route for Prometheus
            _ = builder.Services.UseHttpClientMetrics();

            return builder;
        }

        private static void ConfigureMeter(MeterProviderBuilder builder)
        {
            _ = builder.AddMeter("ReverseProxy");
            _ = builder.AddPrometheusExporter();
        }

        public static IServiceCollection AddOpenTelemetryServices(this IServiceCollection services)
        {
            return services.AddMemoryCache();
        }

        public static IApplicationBuilder UseCustomMetrics(this WebApplication app)
        {
            // Add middleware to handle scraping requests for metrics
            _ = app.UseHttpMetrics();

            // Enable the /metrics page to export Prometheus metrics
            _ = app.MapMetrics();

            return app.UseMiddleware<AzureOpenAICustomMetricsPublisherMiddleware>();
        }
    }
}
