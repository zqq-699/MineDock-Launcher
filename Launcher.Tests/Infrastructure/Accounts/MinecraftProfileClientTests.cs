using Launcher.Infrastructure.Accounts;
using Launcher.Application.Accounts;
using System.Net;
using System.Net.Http.Headers;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        var source = await cache.StoreUploadedSkinAsync("uuid", skinPath, MinecraftSkinModel.Classic, CancellationToken.None);

        Assert.NotNull(source);
        var cachedPath = new Uri(source).LocalPath;
        Assert.True(File.Exists(cachedPath));
        Assert.StartsWith(Path.Combine(cacheDirectory, "uuid"), cachedPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(await File.ReadAllBytesAsync(skinPath), await File.ReadAllBytesAsync(cachedPath));
    }

    [Fact]
    public async Task AccountSkinCacheServiceImportDeduplicatesByHashAndModel()
    {
        Directory.CreateDirectory(tempRoot);
        var skinPath = Path.Combine(tempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, [0x89, 0x50, 0x4E, 0x47]);
        var cache = new AccountSkinCacheService(new HttpClient(new ThrowingHandler()), Path.Combine(tempRoot, "cache"));
        var account = new LauncherAccount
        {
            Id = "microsoft-uuid",
            DisplayName = "Player",
            Uuid = "uuid",
            IsOffline = false
        };
        var first = await cache.ImportSkinAsync(account, skinPath, MinecraftSkinModel.Classic, CancellationToken.None);
        var accountWithSkin = AccountMapper.WithSkinLibrary(account, [first], null, null, null);

        var second = await cache.ImportSkinAsync(accountWithSkin, skinPath, MinecraftSkinModel.Classic, CancellationToken.None);
        var slim = await cache.ImportSkinAsync(accountWithSkin, skinPath, MinecraftSkinModel.Slim, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.Id, slim.Id);
        Assert.Equal(MinecraftSkinModel.Slim, slim.SkinModel);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(tempRoot, "cache", "uuid"), "*.png"));
    }

    [Fact]
    public async Task AccountSkinCacheServiceDeduplicatesEquivalentSkinPixels()
    {
        Directory.CreateDirectory(tempRoot);
        var firstSkinPath = Path.Combine(tempRoot, "skin-original.png");
        var secondSkinPath = Path.Combine(tempRoot, "skin-with-comment.png");
        await File.WriteAllBytesAsync(firstSkinPath, CreateSkinPngBytes());
        await File.WriteAllBytesAsync(secondSkinPath, CreateSkinPngBytes("same-pixels"));
        var cache = new AccountSkinCacheService(new HttpClient(new ThrowingHandler()), Path.Combine(tempRoot, "cache"));
        var account = new LauncherAccount
        {
            Id = "microsoft-uuid",
            DisplayName = "Player",
            Uuid = "uuid",
            IsOffline = false
        };
        var first = await cache.ImportSkinAsync(account, firstSkinPath, MinecraftSkinModel.Classic, CancellationToken.None);
        var accountWithSkin = AccountMapper.WithSkinLibrary(account, [first], null, null, null);

        var second = await cache.ImportSkinAsync(accountWithSkin, secondSkinPath, MinecraftSkinModel.Classic, CancellationToken.None);

        var firstBytes = await File.ReadAllBytesAsync(firstSkinPath);
        var secondBytes = await File.ReadAllBytesAsync(secondSkinPath);
        Assert.False(firstBytes.SequenceEqual(secondBytes));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.Source, second.Source);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(tempRoot, "cache", "uuid"), "*.png"));
    }

    [Fact]
    public async Task AccountSkinCacheServiceAvailableSkinsFollowAccountDirectoryFiles()
    {
        Directory.CreateDirectory(tempRoot);
        var cacheDirectory = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(cacheDirectory);
        var legacySkinPath = Path.Combine(cacheDirectory, "uuid-v1-legacy.png");
        await File.WriteAllBytesAsync(legacySkinPath, [0x89, 0x50, 0x4E, 0x47]);
        var cache = new AccountSkinCacheService(new HttpClient(new ThrowingHandler()), cacheDirectory);
        var account = new LauncherAccount
        {
            Id = "microsoft-uuid",
            DisplayName = "Player",
            Uuid = "uuid",
            IsOffline = false,
            SkinLibrary =
            [
                new LauncherSkinRecord
                {
                    Id = "skin-existing",
                    Source = legacySkinPath,
                    SkinModel = MinecraftSkinModel.Classic,
                    AddedAtUtc = DateTimeOffset.UnixEpoch
                },
                new LauncherSkinRecord
                {
                    Id = "skin-missing",
                    Source = Path.Combine(cacheDirectory, "missing.png"),
                    SkinModel = MinecraftSkinModel.Slim,
                    ContentHash = "missing-hash",
                    AddedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1)
                }
            ]
        };

        var availableSkins = cache.GetAvailableSkins(account);

        var accountSkinDirectory = Path.Combine(cacheDirectory, "uuid");
        var skin = Assert.Single(availableSkins);
        Assert.Equal("skin-existing", skin.Id);
        Assert.Equal(MinecraftSkinModel.Classic, skin.SkinModel);
        Assert.StartsWith(accountSkinDirectory, new Uri(skin.Source).LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.EnumerateFiles(accountSkinDirectory, "*.png"));
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

    private static byte[] CreateSkinPngBytes(string? comment = null)
    {
        const int width = 64;
        const int height = 64;
        const int bytesPerPixel = 4;
        var pixels = new byte[width * height * bytesPerPixel];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * bytesPerPixel;
                pixels[index] = (byte)(x * 3);
                pixels[index + 1] = (byte)(y * 3);
                pixels[index + 2] = 180;
                pixels[index + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * bytesPerPixel);
        var encoder = new PngBitmapEncoder();
        var metadata = new BitmapMetadata("png");
        if (!string.IsNullOrWhiteSpace(comment))
            metadata.SetQuery("/tEXt/{str=Comment}", comment);

        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

