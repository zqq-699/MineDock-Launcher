using System.Net;
using Launcher.Infrastructure.Accounts;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AccountCapeCacheServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"launcher-cape-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetOrCreateCapeSourceAsyncDownloadsCapeToLocalCache()
    {
        var capeBytes = new byte[] { 1, 2, 3, 4 };
        var service = new AccountCapeCacheService(
            new HttpClient(new BinaryResponseHandler(capeBytes)),
            tempRoot);

        var source = await service.GetOrCreateCapeSourceAsync(
            "account-1",
            "cape-1",
            "https://textures.example/cape.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(source);
        var uri = new Uri(source);
        Assert.True(uri.IsFile);
        Assert.True(File.Exists(uri.LocalPath));
        Assert.Equal(capeBytes, await File.ReadAllBytesAsync(uri.LocalPath));
    }

    [Fact]
    public async Task GetOrCreateCapeSourceAsyncUsesCachedCapeWhenDownloadFails()
    {
        var service = new AccountCapeCacheService(
            new HttpClient(new ThrowingResponseHandler()),
            tempRoot);
        var firstSource = await new AccountCapeCacheService(
                new HttpClient(new BinaryResponseHandler([9, 8, 7])),
                tempRoot)
            .GetOrCreateCapeSourceAsync(
                "account-1",
                "cape-1",
                "https://textures.example/cape.png",
                forceRefresh: true,
                CancellationToken.None);

        var source = await service.GetOrCreateCapeSourceAsync(
            "account-1",
            "cape-1",
            "https://textures.example/cape.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.Equal(firstSource, source);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class BinaryResponseHandler(byte[] responseBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes)
            });
        }
    }

    private sealed class ThrowingResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("download failed");
        }
    }
}
