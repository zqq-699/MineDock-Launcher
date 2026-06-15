using System.Net;

namespace Launcher.Tests.Helpers;

internal sealed class CaptureHandler : HttpMessageHandler
{
    private readonly string responseBody;

    public CaptureHandler(string responseBody)
    {
        this.responseBody = responseBody;
    }

    public Uri? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request.RequestUri;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody)
        };
        return Task.FromResult(response);
    }
}

