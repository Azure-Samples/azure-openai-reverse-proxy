using Proxy.OpenAI;
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

        public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
        {
            TimeSpan reactivationPeriod = GetReactivationPeriod(context.Response.Headers);

            DestinationHealth newHealthState = GetDestinationHealthState(context.Response, cluster.Model.Config.Metadata);

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
            IReadOnlyDictionary<string, string>? clusterMetadata)
        {
            if (response.StatusCode is >= 400 and <= 599)
            {
                return DestinationHealth.Unhealthy;
            }

            if (!ClusterHasMetadata(clusterMetadata))
            {
                return DestinationHealth.Healthy;
            }

            (int, int) thresholds = GetThresholdsFromMetadata(clusterMetadata);
            (int, int) remainingCapacity = OpenAIRemainingCapacityParser.GetAzureOpenAIRemainingCapacity(response);

            logger.RemainingCapacity(remainingCapacity.Item1, remainingCapacity.Item2);

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
    }

    internal static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            EventName = nameof(RemainingCapacity),
            Level = LogLevel.Information,
            Message = "Remaining requests and tokens: {remainingRequests}/{remainingTokens}")]
        public static partial void RemainingCapacity(this ILogger logger, int remainingRequests, int remainingTokens);
    }
}
