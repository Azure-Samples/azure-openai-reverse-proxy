using Microsoft.Extensions.Caching.Memory;
using Prometheus;
using Proxy.Models;
using Proxy.OpenAI;
using Proxy.Transformers;

namespace Proxy.Telemetry;

public class PrometheusPublisherMiddleware(RequestDelegate next)
{
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
                PrometheusMetrics.FailedHttpRequestsCounter
                    .WithLabels(accountName, deploymentName, $"{context.Response.StatusCode}")
                    .Inc(1);
            }
            else
            {
                (int, int) remainingCapacity = OpenAIRemainingCapacityParser.GetAzureOpenAIRemainingCapacity(context.Response);

                PrometheusMetrics.RemainingRequestsGauge
                    .WithLabels(accountName, deploymentName)
                    .Set(remainingCapacity.Item1);

                PrometheusMetrics.RemainingTokensGauge
                    .WithLabels(accountName, deploymentName)
                    .Set(remainingCapacity.Item2);
            }
        }
    }

    private static ResourceDetails GetResourceDetailsFromDestination(IMemoryCache cache, string destinationAddress)
    {
        if (cache.TryGetValue(destinationAddress, out ResourceDetails resourceDetails))
            return resourceDetails;

        Uri uri = new(destinationAddress);

        string accountName = uri.Host.Split('.')[0].Replace('-', '_');

        string[] pathSegments = uri.AbsolutePath.Trim('/').Split('/');
        string deploymentName = pathSegments[^1].Replace('-', '_');

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

public static class PrometheusPublisherMiddlewareExtensions
{
    public static IServiceCollection AddPrometheusServices(this IServiceCollection services)
    {
        return services.AddMemoryCache();
    }

    public static IApplicationBuilder UsePrometheusMetrics(this WebApplication app)
    {
        // Add middleware to handle scraping requests for metrics
        app.UseHttpMetrics();

        // Enable the /metrics page to export Prometheus metrics
        app.MapMetrics();

        return app.UseMiddleware<PrometheusPublisherMiddleware>();
    }
}
