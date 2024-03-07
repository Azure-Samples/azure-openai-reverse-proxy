# Azure OpenAI Reverse Proxy

A [reverse proxy](https://en.wikipedia.org/wiki/Reverse_proxy) for distributing requests across OpenAI model deployments (e.g. GPT-4) hosted in Azure OpenAI Service (AOAI).

![Architecture](./images/architecture-overview.png)

> [!IMPORTANT]
> This is a highly experimental solution, and it's not an official Microsoft product.

## Table of contents

- [Problem statement](#problem-statement)
- [Solution](#solution)
  - [Core features](#core-features)
  - [Passive Health Check](#passive-health-check)
  - [Metrics](#metrics)
  - [Use Cases](#use-cases)
  - [Limitations](#limitations)
- [Trying it out](#trying-it-out)
  - [Prerequisites](#prerequisites)
  - [Proxy configuration options](#proxy-configuration-options)
    - [YARP-based configuration](#yarp-based-configuration)
    - [Model deployments discovery configuration](#model-deployments-discovery-configuration)
  - [App settings setup](#app-settings-setup)
  - [Running the solution](#running-the-solution)
  - [Testing the proxy](#testing-the-proxy)
  - [Environment teardown](#environment-teardown)
- [References](#references)

## Problem Statement

An Azure OpenAI deployment model throttling is designed taking into consideration two configurable rate limits:

- `Tokens-per-minute (TPM)`: Estimated number of tokens that can processed over a one-minute period
- `Requests-per-minute (RPM)`: Estimated number of requests over a one-minute period

A deployment model is considered overloaded when _at least_ one of these rate limits is reached, and Azure OpenAI returns an HTTP 429 ("Too Many Requests") response code to the client with a "Retry-After" HTTP header indicating how many seconds the deployment model will be unavailable before starting to accept more requests.

### Challenges

What if there is an increasing demand for requests and/or tokens that can't be met with the deployment model's rate limits? Currently the alternatives are:

1. Increase the model deployment capacity by requesting [Provisioned throughput units (PTU)](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/provisioned-throughput).

2. Build a load balancing component to distribute the requests to the model deployments, hosted on a single or multiple Azure OpenAI resources, optimizing resources utilization and maximizing throughput.

3. Adopt a failover strategy by forwarding the requests from an overloaded model deployment to another one.

These approaches can be combined to achieve enhanced scalability, performance and availability.

## Solution

This repository showcases a proof-of-concept solution for the alternative #2: A reverse proxy built in ASP.NET Core with [YARP](https://microsoft.github.io/reverse-proxy/articles/getting-started.html).

### Core features

- YARP's built-in [load balancing algorithms](https://microsoft.github.io/reverse-proxy/articles/load-balancing.html#built-in-policies).

* Custom [passive health check](https://microsoft.github.io/reverse-proxy/articles/dests-health-checks.html#passive-health-checks) to reactively evaluate and assign states for Azure OpenAI model deployments. For more info, check out the [Passive Health Check](#passive-health-check) section.

* Built-in custom Prometheus metrics to measure and collect insights on how the reverse proxy is performing on distributing the requests. For more info, see the [Metrics](#metrics) section.

### Passive Health Check

> WIP

### Metrics

> WIP

### Use Cases

The reverse proxy can be used as:

1. A gateway to serve as an entrypoint for one or more LLM apps;
2. A [sidecar](https://learn.microsoft.com/en-us/azure/architecture/patterns/sidecar) app to run alongside an LLM app (e.g. in a Kubernetes environment such as Azure Kubernetes Service or Azure Container Apps).

### Limitations

- The solution currently supports only 1 Azure OpenAI Service resource with multiple model deployments.
- Currently there's no concept of priority groups of weights to model deployments (e.g. prioritizing PTU-based deployments).

## Trying it out

The repository provides the following containerized services out of the box to simplify local development:

![Containerized environment](./images/containerized-environment.png)

### Prerequisites

- An Azure OpenAI Service with 2 or more model deployments. For more information about model deployment, see the [resource deployment guide](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/create-resource?pivots=web-portal).
- [Docker](https://docs.docker.com/get-docker/), or [Podman](https://podman.io/docs/installation) with [podman-compose](https://github.com/containers/podman-compose).

### Proxy configuration options

Create an `appsettings.Local.json` file on `src/proxy` directory to start the proxy configuration for your local environment. There are two options to configure the load balancer and passive health check:

1. Using YARP's built-in [ReverseProxy](https://microsoft.github.io/reverse-proxy/articles/config-files.html#configuration-structure) config section to manually set the route and cluster. Check out the [YARP-based configuration](#yarp-based-configuration) section for a config sample.

2. Using a `ModelDeploymentsDiscovery` config section to dynamically discover model deployments on the Azure OpenAI resource tailored to your filter pattern (e.g. discovering only GPT 3.5 deployments via `gpt-35*` pattern) and create the route and cluster properties. Check out the [Model deployments discovery configuration] section for a config sample.

#### YARP-based configuration

```
{
  "ReverseProxy": {
    "Routes": {
      "route1": {
        "ClusterId": "cluster1",
        "Match": {
          "Path": "{**catch-all}"
        }
      }
    },
    "Clusters": {
      "cluster1": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Passive": {
            "Enabled": "true",
            "Policy": "AzureOpenAIPassiveHealthCheckPolicy",
          }
        },
        "Metadata": {
          "RemainingRequestsThreshold": "100",
          "RemainingTokensThreshold": "1000"
        },
        "Destinations": {
          "deployment1": {
            "Address": "https://my-account.openai.azure.com/openai/deployments/deployment-1"
          },
          "deployment2": {
            "Address": "https://my-account.openai.azure.com/openai/deployments/deployment-2"
          }
        }
      }
    }
  }
}
```

#### Model deployments discovery configuration

```
{
  "ModelDeploymentsDiscovery": {
    "SubscriptionId": "<subscription id>",
    "ResourceGroupName": "<resource group name",
    "AccountId": "<azure openai account name>",

    "FilterPattern": "gpt-35*",
    "FrequencySeconds": 5,

    "LoadBalancingPolicy": "RoundRobin",
    "PassiveHealthCheck": {
      "Policy": "AzureOpenAIPassiveHealthCheckPolicy",
      "Metadata": {
        "RemainingRequestsThreshold": "100",
        "RemainingTokensThreshold": "1000"
      }
    }
  }
}
```

### App settings setup

Create a `.env` file on the root directory and add the Azure OpenAI API key:

```
AZURE_OPENAI_API_KEY=<api-key>
```

> The `PROXY_ENDPOINT` environment variable is set by default on the `compose.yml` file.

### Running the reverse proxy

Spin services up with Docker compose:

```sh
docker-compose up
```

> [!IMPORTANT]
> For any code changes, make sure you build the image again before running using the `--build` flag:` docker-compose up --build`

### Testing the proxy

The repository provides the following ways of sending HTTP requests to Azure OpenAI Chat Completions API through the proxy:

1. Sequential requests via bash script, available on the `scripts` folder:

   ```
   ./scripts/client.sh
   ```

   or via powershell

   ```
   .\scripts\client.ps1
   ```

2. Concurrent requests via `k6`, a load testing tool:

   ```
   docker-compose run k6 run /scripts/client.js
   ```

### Environment teardown

For stopping and removing the containers, networks, volumes and images:

```
docker-compose down --volumes --rmi all
```

## References

- [Understanding rate limits](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/quota?tabs=rest#understanding-rate-limits)

* [OpenAI: Rate limits in headers](https://platform.openai.com/docs/guides/rate-limits/rate-limits-in-headers)
