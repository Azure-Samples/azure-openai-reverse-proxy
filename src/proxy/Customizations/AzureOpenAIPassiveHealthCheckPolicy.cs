using Microsoft.Extensions.Primitives;
using Proxy.Customizations.Exceptions;
using Proxy.Models;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace Proxy.Customizations
{
    public class AzureOpenAIPassiveHealthCheckPolicy(
        IDestinationHealthUpdater healthUpdater,
        ILogger<AzureOpenAIPassiveHealthCheckPolicy> logger) : IPassiveHealthCheckPolicy
    {
        public string Name => nameof(AzureOpenAIPassiveHealthCheckPolicy);
        public const string HttpClientName = nameof(AzureOpenAIPassiveHealthCheckPolicy);

        private static readonly TimeSpan _defaultReactivationPeriod = TimeSpan.FromSeconds(6);

        private static readonly Action<ILogger, int, int, Exception?> RemainingCapacity =
            LoggerMessage.Define<int, int>(
                LogLevel.Information,
                new EventId(1, nameof(RemainingCapacity)),
                "Remaining requests and tokens: {RemainingRequests}/{RemainingTokens}");

        public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
        {
            TimeSpan reactivationPeriod = GetReactivationPeriod(context.Response.Headers);

            DestinationHealth newHealthState = GetDestinationHealthState(context.Response, cluster.Model.Config.Metadata, destination.Model.Config.Address);

            healthUpdater.SetPassive(cluster, destination, newHealthState, reactivationPeriod);
        }

        private static TimeSpan GetReactivationPeriod(IHeaderDictionary responseHeaders)
        {
            TimeSpan reactivationPeriod = _defaultReactivationPeriod;

            string retryAfterHeader = responseHeaders.RetryAfter.ToString();

            if (double.TryParse(retryAfterHeader, out double retryAfterSeconds))
            {
                reactivationPeriod = TimeSpan.FromSeconds(retryAfterSeconds);
            }

            return reactivationPeriod;
        }

        private DestinationHealth GetDestinationHealthState(
            HttpResponse response,
            IReadOnlyDictionary<string, string>? clusterMetadata,
            string destinationAddress)
        {
            string accountName = GetAccountNameFromDestination(destinationAddress);
            string deploymentName = GetDeploymentNameFromDestination(destinationAddress);

            if (response.StatusCode is >= 400 and <= 599)
            {
                PrometheusMetrics.FailedHttpRequestsCounter
                    .WithLabels(accountName, deploymentName, $"{response.StatusCode}")
                    .Inc(1);

                return DestinationHealth.Unhealthy;
            }

            if (!ClusterHasMetadata(clusterMetadata))
            {
                return DestinationHealth.Healthy;
            }

            (int, int) thresholds = GetThresholdsFromMetadata(clusterMetadata);
            (int, int) remainingCapacity = GetAzureOpenAIRemainingCapacity(response);

            PrometheusMetrics.RemainingRequestsGauge
                .WithLabels(accountName, deploymentName)
                .Set(remainingCapacity.Item1);

            PrometheusMetrics.RemainingTokensGauge
                .WithLabels(accountName, deploymentName)
                .Set(remainingCapacity.Item2);


            RemainingCapacity(logger, remainingCapacity.Item1, remainingCapacity.Item2, null);

            bool isValidRemainingRequests = remainingCapacity.Item1 > thresholds.Item1;
            bool isValidRemainingTokens = remainingCapacity.Item2 > thresholds.Item2;

            return isValidRemainingRequests && isValidRemainingTokens
                ? DestinationHealth.Healthy
                : DestinationHealth.Unhealthy;
        }

        private static bool ClusterHasMetadata(IReadOnlyDictionary<string, string>? clusterMetadata)
        {
            return clusterMetadata != null
              && (clusterMetadata.ContainsKey("RemainingRequestsThreshold") || clusterMetadata.ContainsKey("remainingTokensThreshold"));
        }

        private static (int, int) GetThresholdsFromMetadata(IReadOnlyDictionary<string, string>? clusterMetadata)
        {
            return clusterMetadata == null || clusterMetadata.Count == 0
                ? throw new ArgumentException("Cluster metadata cannot be null or empty.")
                : !clusterMetadata.TryGetValue("RemainingRequestsThreshold", out string? remainingRequestsThresholdValue)
                ? throw new ArgumentException("Cluster 'RemainingRequestsThreshold' metadata parameter must be set.")
                : !int.TryParse(remainingRequestsThresholdValue, out int remainingRequestsThreshold)
                ? throw new ArgumentException("Cluster 'RemainingRequestsThreshold' metadata parameter value must be integer.")
                : !clusterMetadata.TryGetValue("RemainingTokensThreshold", out string? remainingTokensThresholdValue)
                ? throw new ArgumentException("Cluster 'RemainingTokensThreshold' metadata parameter must be set.")
                : !int.TryParse(remainingTokensThresholdValue, out int remainingTokensThreshold)
                ? throw new ArgumentException("Cluster 'RemainingTokensThreshold' metadata parameter value must be integer.")
                : ((int, int))(remainingRequestsThreshold, remainingTokensThreshold);
        }

        private static (int, int) GetAzureOpenAIRemainingCapacity(HttpResponse response)
        {
            if (!response.Headers.TryGetValue("x-ratelimit-remaining-requests", out StringValues remainingRequestsValue))
            {
                throw new MissingHeaderException("Could not collect the Azure OpenAI x-ratelimit-remaining-requests header attribute.");
            }

            if (!int.TryParse(remainingRequestsValue, out int remainingRequests))
            {
                throw new MissingHeaderException("The Azure OpenAI x-ratelimit-remaining-requests header value is not integer.");
            }

            // Requests limit is returned by 10s, so we need to convert to requests/min
            remainingRequests *= 6;

            return !response.Headers.TryGetValue("x-ratelimit-remaining-tokens", out StringValues remainingTokensValue)
                ? throw new MissingHeaderException("Could not collect the Azure OpenAI x-ratelimit-remaining-tokens header attribute.")
                : !int.TryParse(remainingTokensValue, out int remainingTokens)
                ? throw new MissingHeaderException("The Azure OpenAI x-ratelimit-remaining-tokens header value is not integer.")
                : ((int, int))(remainingRequests, remainingTokens);
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
}
