using Microsoft.Extensions.Primitives;
using Proxy.Exceptions;

namespace Proxy.OpenAI;

public static class OpenAIRemainingCapacityParser
{
    public static (int, int) GetAzureOpenAIRemainingCapacity(HttpResponse response)
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
}
