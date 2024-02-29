using Proxy.Models;
using Proxy.OpenAI;
using Proxy.Transformers;

namespace Proxy.Telemetry;

public class PrometheusPublisherMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (context.Response.Headers.TryGetValue(AbsoluteUriResponseTransformer.AbsoluteUriHeaderKey, out var values))
        {
            string destinationAddress = values.ToString();

            string accountName = GetAccountNameFromDestination(destinationAddress);
            string deploymentName = GetDeploymentNameFromDestination(destinationAddress);

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

    private static string GetDeploymentNameFromDestination(string destinationAddress)
    {
        Uri uri = new(destinationAddress);

        string[] pathSegments = uri.AbsolutePath.Trim('/').Split('/');
        return pathSegments[^1].Replace('-', '_');
    }

    private static string GetAccountNameFromDestination(string destinationAddress)
    {
        Uri uri = new(destinationAddress);
        return uri.Host.Split('.')[0].Replace('-', '_');
    }
}

public static class PrometheusPublisherMiddlewareExtensions
{
    public static IApplicationBuilder UsePrometheusPublisherMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PrometheusPublisherMiddleware>();
    }
}
