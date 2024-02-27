using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace Proxy.Policies.HealthChecks;

public class TooManyRequestsPolicy(IDestinationHealthUpdater healthUpdater) : IPassiveHealthCheckPolicy
{
    public string Name => typeof(TooManyRequestsPolicy).Name;

    private static readonly TimeSpan _defaultReactivationPeriod = TimeSpan.FromSeconds(6);

    public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
    {
        DestinationHealth newHealth = context.Response.StatusCode == 429 
            ? DestinationHealth.Unhealthy 
            : DestinationHealth.Healthy;

        TimeSpan reactivationPeriod = _defaultReactivationPeriod;
        string? retryAfterHeader = context.Response.Headers.RetryAfter.ToString();

        if (double.TryParse(retryAfterHeader, out var retryAfterSeconds))
        {
            reactivationPeriod = TimeSpan.FromSeconds(retryAfterSeconds);
        }

        healthUpdater.SetPassive(cluster, destination, newHealth, reactivationPeriod);
    }
}