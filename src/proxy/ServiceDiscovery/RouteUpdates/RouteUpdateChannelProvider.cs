using System.Threading.Channels;

namespace Proxy.ServiceDiscovery.RouteUpdates
{
    internal sealed class RouteUpdateChannelProvider
    {
        private readonly Channel<RouteUpdate> channel;

        public RouteUpdateChannelProvider()
        {
            channel = Channel.CreateUnbounded<RouteUpdate>(
                new UnboundedChannelOptions
                {
                    SingleWriter = false,
                    SingleReader = true,
                });
        }

        internal ChannelReader<RouteUpdate> ChannelReader => channel.Reader;
        internal ChannelWriter<RouteUpdate> ChannelWriter => channel.Writer;
    }
}
