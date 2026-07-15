using System.Net;
using System.Net.Http;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackMetadataHostConcurrencyTests
{
    [Fact]
    public async Task ModrinthRateLimitReducesOnlyModrinthHostTarget()
    {
        var controller = CreateController();
        using var httpClient = new HttpClient(new CallbackHandler(request =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([])
            }));
        var client = new ModrinthApiClient(
            httpClient,
            new ImportConcurrencyLimiter(),
            logger: null,
            controller);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetVersionFileMatchesAsync([new string('a', 40)], CancellationToken.None));

        Assert.Equal(
            32,
            controller.GetSnapshot("https://api.modrinth.com:443").CurrentTarget);
        Assert.Equal(
            64,
            controller.GetSnapshot("https://api.curseforge.com:443").CurrentTarget);
    }

    [Fact]
    public async Task CurseForgeExpectedForbiddenDownloadUrlIsNeutral()
    {
        var controller = CreateController();
        using var httpClient = new HttpClient(new CallbackHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download-url", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent([])
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("""
                    {"data":{"displayName":"Example","fileName":"example.jar","downloadUrl":null,"hashes":[]}}
                    """)
            };
        }));
        var client = new CurseForgeApiClient(
            httpClient,
            new ImportConcurrencyLimiter(),
            logger: null,
            controller);

        var result = await client.GetFileDownloadAsync(123, 456, "test-key", CancellationToken.None);

        Assert.Equal("example.jar", result.FileName);
        Assert.Equal(64, controller.GetSnapshot("https://api.curseforge.com:443").CurrentTarget);
    }

    private static DownloadHostConcurrencyController CreateController() => new(
        maximumJitter: TimeSpan.Zero,
        nextJitter: () => 0,
        delayAsync: static (_, _) => ValueTask.CompletedTask);

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(request));
    }
}
