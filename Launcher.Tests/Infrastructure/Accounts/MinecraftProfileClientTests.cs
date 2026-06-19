using Launcher.Infrastructure.Accounts;
using Launcher.Application.Accounts;
using System.Net;
using System.Net.Http.Headers;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MinecraftProfileClientTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"launcher-profile-client-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(MinecraftSkinModel.Classic, "classic")]
    [InlineData(MinecraftSkinModel.Slim, "slim")]
    public async Task UploadSkinAsyncPostsMultipartSkinWithSelectedVariant(
        MinecraftSkinModel skinModel,
        string expectedVariant)
    {
        Directory.CreateDirectory(tempRoot);
        var skinPath = Path.Combine(tempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, [0x89, 0x50, 0x4E, 0x47]);
        var handler = new UploadSkinCaptureHandler();
        var client = new MinecraftProfileClient(new HttpClient(handler));

        await client.UploadSkinAsync("access-token", skinPath, skinModel, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile/skins", handler.RequestUri?.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), handler.Authorization);
        Assert.Contains("name=variant", handler.Content);
        Assert.Contains(expectedVariant, handler.Content);
        Assert.Contains("name=file", handler.Content);
        Assert.Contains("filename=skin.png", handler.Content);
    }

    [Fact]
    public async Task GetProfileAsyncReadsActiveSkinVariant()
    {
        var handler = new ProfileResponseHandler(
            """
            {
              "id": "00000000000000000000000000000001",
              "name": "Player",
              "skins": [
                { "state": "INACTIVE", "url": "https://example.com/old.png", "variant": "classic" },
                { "state": "ACTIVE", "url": "https://example.com/current.png", "variant": "slim" }
              ]
            }
            """);
        var client = new MinecraftProfileClient(new HttpClient(handler));

        var profile = await client.GetProfileAsync("access-token", CancellationToken.None);

        Assert.Equal(MinecraftSkinModel.Slim, MinecraftAccountHelpers.GetActiveSkinModel(profile));
    }

    [Fact]
    public async Task AccountSkinCacheServiceStoresUploadedSkinAsLocalUri()
    {
        Directory.CreateDirectory(tempRoot);
        var skinPath = Path.Combine(tempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, [0x89, 0x50, 0x4E, 0x47]);
        var cacheDirectory = Path.Combine(tempRoot, "cache");
        var cache = new AccountSkinCacheService(new HttpClient(new ThrowingHandler()), cacheDirectory);

        var source = await cache.StoreUploadedSkinAsync("uuid", skinPath, CancellationToken.None);

        Assert.NotNull(source);
        var cachedPath = new Uri(source).LocalPath;
        Assert.True(File.Exists(cachedPath));
        Assert.StartsWith(cacheDirectory, cachedPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(await File.ReadAllBytesAsync(skinPath), await File.ReadAllBytesAsync(cachedPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class UploadSkinCaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string Content { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ProfileResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("HTTP should not be used for uploaded skin cache.");
        }
    }
}

