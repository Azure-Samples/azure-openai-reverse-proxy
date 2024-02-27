using Proxy.Models;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace Proxy.Policies.HealthChecks;

public class RateLimitPolicy(IDestinationHealthUpdater healthUpdater, 
    ILogger<RateLimitPolicy> logger) : IPassiveHealthCheckPolicy
{
    public string Name => nameof(RateLimitPolicy);
    private static readonly TimeSpan _defaultReactivationPeriod = TimeSpan.FromSeconds(6);

    public const string HttpClientName = nameof(RateLimitPolicy);

    public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
    {
        DestinationHealth newHealthState = GetDestinationHealthState(context.Response, cluster.Model.Config.Metadata, destination.Model.Config.Address);

        TimeSpan reactivationPeriod = _defaultReactivationPeriod;
        string? retryAfterHeader = context.Response.Headers.RetryAfter.ToString();

        if (double.TryParse(retryAfterHeader, out var retryAfterSeconds))
            reactivationPeriod = TimeSpan.FromSeconds(retryAfterSeconds);

        healthUpdater.SetPassive(cluster, destination, newHealthState, reactivationPeriod);
    }

    private DestinationHealth GetDestinationHealthState(
        HttpResponse response, 
        IReadOnlyDictionary<string, string>? clusterMetadata,
        string destinationAddress)
    {
        string accountName = GetAccountNameFromDestination(destinationAddress);
        string deploymentName = GetDeploymentNameFromDestination(destinationAddress);

        if (response.StatusCode >= 400 && response.StatusCode <= 599)
        {
            PrometheusMetrics.FailedHttpRequestsCounter
                .WithLabels(accountName, deploymentName, response.StatusCode.ToString())
                .Inc(1);

            return DestinationHealth.Unhealthy;
        }
            
        (int, int) thresholds = GetThresholdsFromMetadata(clusterMetadata);
        (int, int) remainingCapacity = GetAzureOpenAIRemainingCapacity(response);

        PrometheusMetrics.RemainingRequestsGauge
            .WithLabels(accountName, deploymentName)
            .Set(remainingCapacity.Item1);

        PrometheusMetrics.RemainingTokensGauge
            .WithLabels(accountName, deploymentName)
            .Set(remainingCapacity.Item2);

        logger.LogInformation("Remaining requests and tokens: {}/{}", remainingCapacity.Item1, remainingCapacity.Item2);

        bool isValidRemainingRequests = remainingCapacity.Item1 > thresholds.Item1;
        bool isValidRemainingTokens = remainingCapacity.Item2 > thresholds.Item2;

        return isValidRemainingRequests && isValidRemainingTokens
            ? DestinationHealth.Healthy 
            : DestinationHealth.Unhealthy;
    }

    private static (int, int) GetThresholdsFromMetadata(IReadOnlyDictionary<string, string>? clusterMetadata)
    {
        if (clusterMetadata == null || clusterMetadata.Count == 0)
        {
            throw new Exception("Cluster metadata cannot be null or empty.");
        }

        if (!clusterMetadata.TryGetValue("RemainingRequestsThreshold", out var remainingRequestsThresholdValue))
        {
            throw new Exception("Cluster 'RemainingRequestsThreshold' metadata parameter must be set.");
        }

        if (!int.TryParse(remainingRequestsThresholdValue, out int remainingRequestsThreshold))
        {
            throw new Exception("Cluster 'RemainingRequestsThreshold' metadata parameter value must be integer.");
        }

        if (!clusterMetadata.TryGetValue("RemainingTokensThreshold", out var remainingTokensThresholdValue))
        {
            throw new Exception("Cluster 'RemainingTokensThreshold' metadata parameter must be set.");
        }

        if (!int.TryParse(remainingTokensThresholdValue, out int remainingTokensThreshold))
        {
            throw new Exception("Cluster 'RemainingTokensThreshold' metadata parameter value must be integer.");
        }

        return (remainingRequestsThreshold, remainingTokensThreshold);
    }

    private static (int, int) GetAzureOpenAIRemainingCapacity(HttpResponse response)
    {
        if (!response.Headers.TryGetValue("x-ratelimit-remaining-requests", out var remainingRequestsValue))
        {
            throw new Exception("Could not collect the Azure OpenAI x-ratelimit-remaining-requests header attribute.");
        }

        if (!int.TryParse(remainingRequestsValue, out int remainingRequests))
        {
            throw new Exception("The Azure OpenAI x-ratelimit-remaining-requests header value is not integer.");
        }

        // Requests limit is returned by 10s, so we need to convert to requests/min
        remainingRequests *= 6;

        if (!response.Headers.TryGetValue("x-ratelimit-remaining-tokens", out var remainingTokensValue))
        {
            throw new Exception("Could not collect the Azure OpenAI x-ratelimit-remaining-tokens header attribute.");
        }

        if (!int.TryParse(remainingTokensValue, out int remainingTokens))
        {
            throw new Exception("The Azure OpenAI x-ratelimit-remaining-tokens header value is not integer.");
        }

        return (remainingRequests, remainingTokens);
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