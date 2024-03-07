using System.Text.RegularExpressions;
using System.Threading.Channels;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.CognitiveServices;
using Azure.ResourceManager.CognitiveServices.Models;
using Microsoft.Extensions.Options;
using Proxy.Customizations;
using Proxy.ServiceDiscovery.RouteUpdates;
using Yarp.ReverseProxy.Configuration;

namespace Proxy.ServiceDiscovery
{
    internal sealed class PassiveHealthCheckMetadataOptions
    {
        public string? RemainingRequestsThreshold { get; init; }
        public string? RemainingTokensThreshold { get; init; }
    }

    internal sealed class PassiveHealthCheckOptions
    {
        public string Policy { get; init; } = AzureOpenAIPassiveHealthCheckPolicy.PolicyName;
        public PassiveHealthCheckMetadataOptions? Metadata { get; init; }

    }

    internal sealed class AzureOpenAIModelDeploymentsDiscoveryWorkerOptions
    {
        public string SubscriptionId { get; init; } = string.Empty;
        public string ResourceGroupName { get; init; } = string.Empty;
        public string AccountId { get; init; } = string.Empty;
        public string FilterPattern { get; init; } = "gpt-*";
        public int FrequencySeconds { get; init; } = 10;
        public string LoadBalancingPolicy { get; init; } = "RoundRobin";
        public PassiveHealthCheckOptions PassiveHealthCheck { get; init; } = new PassiveHealthCheckOptions();
    }

    internal sealed class AzureOpenAIModelDeploymentsDiscoveryWorker(
        IOptions<AzureOpenAIModelDeploymentsDiscoveryWorkerOptions> options,
        RouteUpdateChannelProvider channelProvider,
        ILogger<AzureOpenAIModelDeploymentsDiscoveryWorker> logger) : BackgroundService
    {
        private readonly AzureOpenAIModelDeploymentsDiscoveryWorkerOptions options = options.Value;
        private readonly ChannelWriter<RouteUpdate> channel = channelProvider.ChannelWriter;
        private readonly ILogger<AzureOpenAIModelDeploymentsDiscoveryWorker> logger = logger;

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            TimeSpan frequencyUpdate = TimeSpan.FromSeconds(options.FrequencySeconds);

            (CognitiveServicesAccountResource azureOpenAIResource, string apiKey) = await GetAzureOpenAIAccountResource(cancellationToken);

            HashSet<Deployment> currentDeployments = [];

            while (!cancellationToken.IsCancellationRequested)
            {
                HashSet<Deployment> discoveredDeployments = await GetDiscoveredDeploymentsAsync(azureOpenAIResource, cancellationToken);
                HashSet<Deployment> deletedDeployments = currentDeployments.Minus(discoveredDeployments);

                if (deletedDeployments.Count > 0)
                {
                    // Refresh proxy destinations config
                    await channel.WriteAsync(new RouteUpdate()
                    {
                        Removed = BuildProxyDestinations(deletedDeployments),
                    }, cancellationToken);

                    logger.LogInformation("deployments deleted: {deleted}", string.Join(',', deletedDeployments.Select(d => d.Name)));

                    // Remove deleted deployments from the list of current deployments
                    currentDeployments.ExceptWith(deletedDeployments);
                }

                HashSet<Deployment> addedDeployments = discoveredDeployments.Minus(currentDeployments);

                if (addedDeployments.Count > 0)
                {
                    logger.LogInformation("deployments added: {added}", string.Join(',', addedDeployments.Select(a => a.Name)));

                    // Newly added deployments may take some time to become fully available for use
                    HashSet<Deployment> notReadyDeployments = await GetNotReadyDeployments(addedDeployments, apiKey, cancellationToken);

                    // Remove not ready deployments from the list of added deployments
                    addedDeployments.ExceptWith(notReadyDeployments);

                    if (addedDeployments.Count > 0)
                    {
                        // Refresh proxy destinations config
                        await channel.WriteAsync(new RouteUpdate()
                        {
                            Added = BuildProxyDestinations(addedDeployments),
                        }, cancellationToken);
                    }

                    currentDeployments.UnionWith(addedDeployments);
                }

                await Task.Delay(frequencyUpdate, cancellationToken);
            }
        }

        private async Task<(CognitiveServicesAccountResource, string)> GetAzureOpenAIAccountResource(CancellationToken cancellationToken)
        {
            ArmClient client = new(new DefaultAzureCredential());

            ResourceIdentifier resourceId = CognitiveServicesAccountResource
              .CreateResourceIdentifier(options.SubscriptionId, options.ResourceGroupName, options.AccountId);

            Response<CognitiveServicesAccountResource> cognitiveSvcResponse = await client
              .GetCognitiveServicesAccountResource(resourceId)
              .GetAsync(cancellationToken);

            CognitiveServicesAccountResource cognitiveServicesAccount = cognitiveSvcResponse.Value;

            Response<ServiceAccountApiKeys> cognitiveSvcKeysResponse = await cognitiveServicesAccount
              .GetKeysAsync(cancellationToken);

            return (cognitiveServicesAccount, cognitiveSvcKeysResponse.Value.Key1);
        }

        private async Task<HashSet<Deployment>> GetDiscoveredDeploymentsAsync(CognitiveServicesAccountResource azureOpenAIResource, CancellationToken cancellationToken)
        {
            HashSet<Deployment> discoveredDeployments = [];

            AsyncPageable<CognitiveServicesAccountDeploymentResource> deployments = azureOpenAIResource
                .GetCognitiveServicesAccountDeployments()
                  .GetAllAsync(cancellationToken);

            string regexPattern = Regex.Escape(options.FilterPattern).Replace("\\*", ".*");
            Regex regex = new(regexPattern);

            await foreach (CognitiveServicesAccountDeploymentResource deployment in deployments)
            {
                Match match = regex.Match(deployment.Data.Properties.Model.Name);

                if (match.Success)
                {
                    _ = discoveredDeployments.Add(new Deployment(options.AccountId, deployment.Data));
                }
            }

            return discoveredDeployments;
        }

        private static Dictionary<string, DestinationConfig> BuildProxyDestinations(HashSet<Deployment> selectedDeployments)
        {
            Dictionary<string, DestinationConfig> destinations = new(StringComparer.OrdinalIgnoreCase);

            foreach (Deployment deployment in selectedDeployments)
            {
                destinations.Add(deployment.Name, new DestinationConfig() { Address = deployment.BaseUrl });
            }

            return destinations;
        }

        private async Task<HashSet<Deployment>> GetNotReadyDeployments(HashSet<Deployment> newDeployments, string apiKey, CancellationToken stoppingToken)
        {
            HashSet<Deployment> notReady = [];

            foreach (Deployment deployment in newDeployments)
            {
                string requestUri = $"{deployment.BaseUrl}/chat/completions?api-version=2023-07-01-preview";

                HttpRequestMessage request = new(HttpMethod.Post, requestUri);
                request.Headers.Add("Api-Key", apiKey);

                HttpResponseMessage response = await new HttpClient().SendAsync(request, stoppingToken);
                string body = await response.Content.ReadAsStringAsync(stoppingToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && body.Contains("DeploymentNotFound"))
                {
                    logger.LogWarning("deployment not ready: {deploy}", deployment.Name);
                    _ = notReady.Add(deployment);
                }
                else
                {
                    logger.LogInformation("deployment ready: {deploy}", deployment.Name);
                }
            }

            return notReady;
        }

        private readonly struct Deployment(string accountId, CognitiveServicesAccountDeploymentData details)
        {
            public string Name { get; } = details.Name;
            public string BaseUrl { get; } = BuildDeploymentBaseUri(accountId, details.Name);

            private static string BuildDeploymentBaseUri(string accountId, string deploymentName)
            {
                return $"https://{accountId}.openai.azure.com/openai/deployments/{deploymentName}";
            }

            public override int GetHashCode()
            {
                return Name.GetHashCode();
            }
        }
    }

    internal static class HashSetExtensions
    {
        public static HashSet<T> Minus<T>(this HashSet<T> A, HashSet<T> B)
        {
            HashSet<T> copy = new(A);
            copy.ExceptWith(B);
            return copy;
        }
    }
}
