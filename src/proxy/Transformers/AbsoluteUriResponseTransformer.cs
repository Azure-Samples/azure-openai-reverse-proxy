using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Proxy.Transformers;

internal static class AbsoluteUriResponseTransformer
{
    internal readonly static string AbsoluteUriHeaderKey = "x-absolute-uri";

    public static void Transform(TransformBuilderContext builderCtx)
    {
        builderCtx.AddResponseTransform(ctx =>
        {
            ctx.HttpContext.Response?.Headers.Append(AbsoluteUriHeaderKey, ctx.ProxyResponse?.RequestMessage?.RequestUri?.AbsoluteUri);
            return ValueTask.CompletedTask;
        });
    }
}
