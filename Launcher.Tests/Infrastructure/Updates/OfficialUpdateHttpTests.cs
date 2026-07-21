using System.Net;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class OfficialUpdateHttpTests
{
    [Fact]
    public async Task GitHubExecutableMayRedirectWithinGithubusercontent()
    {
        const string start = "https://github.com/owner/repo/releases/download/v1/app.exe";
        const string redirected = "https://release-assets.githubusercontent.com/object/app.exe";
        var client = new HttpClient(new RedirectHandler()
            .Redirect(start, redirected)
            .Ok(redirected));

        using var response = await OfficialUpdateHttp.SendAsync(
            client, new Uri(start), OfficialUpdateUriKind.Executable, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MoreThanFiveRedirectsAreRejected()
    {
        var handler = new RedirectHandler();
        for (var index = 0; index <= 5; index++)
        {
            handler.Redirect(
                $"https://github.com/owner/repo/{index}.exe",
                $"https://github.com/owner/repo/{index + 1}.exe");
        }
        var client = new HttpClient(handler);

        await Assert.ThrowsAsync<UpdateSecurityException>(() => OfficialUpdateHttp.SendAsync(
            client, new Uri("https://github.com/owner/repo/0.exe"), OfficialUpdateUriKind.Executable, CancellationToken.None));
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> responses = new(StringComparer.OrdinalIgnoreCase);

        public RedirectHandler Redirect(string url, string location)
        {
            responses[url] = request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request };
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                return response;
            };
            return this;
        }

        public RedirectHandler Ok(string url)
        {
            responses[url] = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([1])
            };
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var response)
                ? response(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }
}
