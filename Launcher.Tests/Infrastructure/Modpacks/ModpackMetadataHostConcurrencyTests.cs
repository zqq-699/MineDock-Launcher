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
        Assert.True(result.IsDistributionRestricted);
        Assert.Equal("https://edge.forgecdn.net/files/0/456/example.jar", result.PrimaryUrl);
        Assert.Equal(["https://mediafilez.forgecdn.net/files/0/456/example.jar"], result.FallbackUrls);
        Assert.Equal(64, controller.GetSnapshot("https://api.curseforge.com:443").CurrentTarget);
    }

    [Fact]
    public async Task CurseForgeDirectDownloadAddsCdnCandidatesWithoutMarkingDistributionRestricted()
    {
        var controller = CreateController();
        using var httpClient = new HttpClient(new CallbackHandler(request =>
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/download-url", StringComparison.Ordinal)
                ? """{"data":"https://download.example/example.jar"}"""
                : """{"data":{"displayName":"Example","fileName":"example.jar","downloadUrl":"https://download.example/example.jar","hashes":[]}}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }));
        var client = new CurseForgeApiClient(
            httpClient,
            new ImportConcurrencyLimiter(),
            logger: null,
            controller);

        var result = await client.GetFileDownloadAsync(123, 456, "test-key", CancellationToken.None);

        Assert.False(result.IsDistributionRestricted);
        Assert.Equal("https://download.example/example.jar", result.PrimaryUrl);
        Assert.Equal(
            [
                "https://edge.forgecdn.net/files/0/456/example.jar",
                "https://mediafilez.forgecdn.net/files/0/456/example.jar"
            ],
            result.FallbackUrls);
    }

    [Fact]
    public async Task CurseForgeTransientMetadataFailureRetriesThreeTimes()
    {
        var controller = CreateController();
        var downloadUrlAttempts = 0;
        var delays = new List<TimeSpan>();
        using var httpClient = new HttpClient(new CallbackHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download-url", StringComparison.Ordinal))
            {
                downloadUrlAttempts++;
                if (downloadUrlAttempts < 3)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("""{"data":"https://download.example/example.jar"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    """{"data":{"displayName":"Example","fileName":"example.jar","downloadUrl":"https://download.example/example.jar","hashes":[]}}""")
            };
        }));
        var client = new CurseForgeApiClient(
            httpClient,
            new ImportConcurrencyLimiter(),
            logger: null,
            controller,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await client.GetFileDownloadAsync(123, 456, "test-key", CancellationToken.None);

        Assert.Equal("example.jar", result.FileName);
        Assert.Equal(3, downloadUrlAttempts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
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
