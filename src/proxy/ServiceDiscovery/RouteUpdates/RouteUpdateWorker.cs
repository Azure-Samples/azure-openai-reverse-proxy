using System.Diagnostics;
using System.Threading.Channels;
using Proxy.Customizations;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Proxy.ServiceDiscovery.RouteUpdates
{
    internal sealed class RouteUpdateWorker(
        RouteUpdateChannelProvider channelProvider,
        InMemoryConfigProvider proxyConfig) : BackgroundService
    {
        private readonly ChannelReader<RouteUpdate> channel = channelProvider.ChannelReader;
        private readonly InMemoryConfigProvider proxyConfig = proxyConfig;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            RouteConfig[] routes = GetInitialRoutes();
            ClusterConfig[] clusters = GetInitialClusters();

            Dictionary<string, DestinationConfig> destinations = [];

            while (!stoppingToken.IsCancellationRequested)
            {
                RouteUpdate update = await channel.ReadAsync(stoppingToken);

                if (update.Removed != null)
                {
                    foreach (KeyValuePair<string, DestinationConfig> d in update.Removed)
                    {
                        _ = destinations.Remove(d.Key);
                    }
                }

                if (update.Added != null)
                {
                    foreach (KeyValuePair<string, DestinationConfig> d in update.Added)
                    {
                        destinations.Add(d.Key, d.Value);
                    }
                }

                clusters = UpdateClusterDestination(clusters, destinations);

                proxyConfig.Update(routes, clusters);
            }
        }

        private static RouteConfig[] GetInitialRoutes(string routeId = "default", string clusterId = "default")
        {
            return [(new RouteConfig()
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch
                {
                    Path = "{**catch-all}"
                },
            })];
        }

        private static ClusterConfig[] GetInitialClusters(string clusterId = "default")
        {
            return
            [
                new ClusterConfig()
                {
                    ClusterId = clusterId,
                    Destinations = null,
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    HealthCheck = new HealthCheckConfig()
                    {
                        Passive = new PassiveHealthCheckConfig()
                        {
                            Enabled = true,
                            Policy = AzureOpenAIPassiveHealthCheckPolicy.PolicyName,
                        }
                    }
                }
            ];
        }

        private static ClusterConfig[] UpdateClusterDestination(ClusterConfig[] clusters, IReadOnlyDictionary<string, DestinationConfig>? destinations, string clusterId = "default")
        {
            int i = Array.FindIndex(clusters, c => c.ClusterId == clusterId);
            Debug.Assert(i >= 0);

            clusters[i] = clusters[i] with { Destinations = destinations };

            return clusters;
        }
    }
}