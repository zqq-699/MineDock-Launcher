using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftHttpClientFactory
{
    public static HttpClient CreateTransportClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public static HttpClientHandler CreateTransportHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
    }
}
