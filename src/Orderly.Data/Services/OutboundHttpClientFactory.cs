namespace Orderly.Data.Services;

public static class OutboundHttpClientFactory
{
    public static HttpClient Create(TimeSpan timeout)
    {
        return new HttpClient(OutboundEndpointPolicy.CreateValidatedHttpMessageHandler())
        {
            Timeout = timeout
        };
    }
}
