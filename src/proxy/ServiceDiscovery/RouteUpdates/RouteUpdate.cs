using Yarp.ReverseProxy.Configuration;

namespace Proxy.ServiceDiscovery.RouteUpdates
{
    internal readonly struct RouteUpdate
    {
        public IReadOnlyDictionary<string, DestinationConfig>? Added { get; init; }
        public IReadOnlyDictionary<string, DestinationConfig>? Removed { get; init; }
    }
}
