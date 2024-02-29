using Prometheus;

namespace Proxy.Models;

public sealed class PrometheusMetrics
{
    public static readonly Gauge RemainingRequestsGauge = Metrics
        .CreateGauge(
            "azure_openai_remaining_requests", 
            "Azure OpenAI remaining requests", 
            new GaugeConfiguration
            {
                LabelNames = ["account_name", "deployment_name"]
            });
    public static readonly Gauge RemainingTokensGauge = Metrics
        .CreateGauge(
            "azure_openai_remaining_tokens", 
            "Azure OpenAI remaining tokens", 
            new GaugeConfiguration
            {
                LabelNames = ["account_name", "deployment_name"]
            });

    public static readonly Counter FailedHttpRequestsCounter = Metrics
        .CreateCounter(
            "azure_openai_failed_http_requests", 
            "Failed requests to Azure OpenAI deployments", 
            new CounterConfiguration
            {
                LabelNames = ["account_name", "deployment_name", "status_code"]
            });
}