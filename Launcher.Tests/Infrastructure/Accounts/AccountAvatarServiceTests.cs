using Launcher.Infrastructure.Accounts;
using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class AccountAvatarServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"launcher-avatar-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncUsesCachedAvatarWhenNotRefreshing()
    {
        var uuid = "00000000000000000000000000000001";
        var avatarPath = Path.Combine(tempRoot, $"{uuid}-576-v6-cached.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(avatarPath, [1, 2, 3]);
        var service = new AccountAvatarService(new HttpClient(), tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: null,
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal(new Uri(avatarPath).AbsoluteUri, avatarSource);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncIgnoresLegacyCachedAvatarWhenNotRefreshing()
    {
        var uuid = "00000000000000000000000000000001";
        var avatarPath = Path.Combine(tempRoot, $"{uuid}-576-v5-cached.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(avatarPath, [1, 2, 3]);
        var service = new AccountAvatarService(new HttpClient(), tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: null,
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal($"https://crafatar.com/avatars/{uuid}?size=576&overlay", avatarSource);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncDoesNotUseCachedAvatarWhenRefreshCannotCreateOne()
    {
        var uuid = "00000000000000000000000000000001";
        var avatarPath = Path.Combine(tempRoot, $"{uuid}-576.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(avatarPath, [1, 2, 3]);
        var service = new AccountAvatarService(new HttpClient(), tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: null,
            forceRefresh: true,
            CancellationToken.None);

        Assert.StartsWith($"https://crafatar.com/avatars/{uuid}?size=576&overlay&t=", avatarSource);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncFallsBackToCrafatarWhenNoCachedAvatarExists()
    {
        var uuid = "00000000000000000000000000000001";
        var service = new AccountAvatarService(new HttpClient(), tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: null,
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal($"https://crafatar.com/avatars/{uuid}?size=576&overlay", avatarSource);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncDoesNotDownloadSkinWhenNotRefreshing()
    {
        var uuid = "00000000000000000000000000000001";
        var service = new AccountAvatarService(new HttpClient(new ThrowingSkinResponseHandler()), tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal($"https://crafatar.com/avatars/{uuid}?size=576&overlay", avatarSource);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncCreatesNewAvatarPathWhenRefreshing()
    {
        var uuid = "00000000000000000000000000000001";
        var oldAvatarPath = Path.Combine(tempRoot, $"{uuid}-576.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(oldAvatarPath, [1, 2, 3]);
        var service = new AccountAvatarService(
            new HttpClient(new SkinResponseHandler(CreateSkinPng())),
            tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(avatarSource);
        var avatarPath = new Uri(avatarSource).LocalPath;
        Assert.NotEqual(oldAvatarPath, avatarPath);
        Assert.StartsWith(Path.Combine(tempRoot, $"{uuid}-576-v6-"), avatarPath);
        Assert.True(File.Exists(avatarPath));
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncDrawsExpandedHeadOverlay()
    {
        var uuid = "00000000000000000000000000000001";
        var face = new Bgra(0x44, 0x88, 0xCC, byte.MaxValue);
        var overlay = new Bgra(0x22, 0x33, 0xEE, byte.MaxValue);
        var service = new AccountAvatarService(
            new HttpClient(new SkinResponseHandler(CreateSkinPng(face, pixels => SetSkinPixel(pixels, 40, 8, overlay)))),
            tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(avatarSource);
        var avatarPath = new Uri(avatarSource).LocalPath;
        Assert.Equal(overlay, ReadAvatarPixel(avatarPath, 10, 10));
        Assert.Equal(face, ReadAvatarPixel(avatarPath, 150, 150));
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncDrawsSoftOverlayShadow()
    {
        var uuid = "00000000000000000000000000000001";
        var face = new Bgra(0x44, 0x88, 0xCC, byte.MaxValue);
        var overlay = new Bgra(0x22, 0x33, 0xEE, byte.MaxValue);
        var service = new AccountAvatarService(
            new HttpClient(new SkinResponseHandler(CreateSkinPng(face, pixels => SetSkinPixel(pixels, 40, 8, overlay)))),
            tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(avatarSource);
        var shadow = ReadAvatarPixel(new Uri(avatarSource).LocalPath, 100, 20);
        Assert.Equal(0, shadow.Blue);
        Assert.Equal(0, shadow.Green);
        Assert.Equal(0, shadow.Red);
        Assert.InRange(shadow.Alpha, 1, 95);
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncKeepsFaceWhenOverlayIsTransparent()
    {
        var uuid = "00000000000000000000000000000001";
        var face = new Bgra(0x44, 0x88, 0xCC, byte.MaxValue);
        var transparentOverlay = new Bgra(0x22, 0x33, 0xEE, 0);
        var service = new AccountAvatarService(
            new HttpClient(new SkinResponseHandler(CreateSkinPng(face, pixels => FillSkinArea(pixels, 40, 8, 8, 8, transparentOverlay)))),
            tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(avatarSource);
        Assert.Equal(face, ReadAvatarPixel(new Uri(avatarSource).LocalPath, 288, 288));
    }

    [Fact]
    public async Task GetOrCreateAvatarSourceAsyncBlendsSemiTransparentOverlay()
    {
        var uuid = "00000000000000000000000000000001";
        var face = new Bgra(100, 120, 140, byte.MaxValue);
        var overlay = new Bgra(200, 40, 20, 128);
        var service = new AccountAvatarService(
            new HttpClient(new SkinResponseHandler(CreateSkinPng(face, pixels => FillSkinArea(pixels, 40, 8, 8, 8, overlay)))),
            tempRoot);

        var avatarSource = await service.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl: "https://textures.example/skin.png",
            forceRefresh: true,
            CancellationToken.None);

        Assert.NotNull(avatarSource);
        Assert.Equal(new Bgra(140, 68, 66, byte.MaxValue), ReadAvatarPixel(new Uri(avatarSource).LocalPath, 288, 288));
    }

    [Fact]
    public async Task DeleteAvatarRemovesSizedAndLegacyCachedFiles()
    {
        var uuid = "00000000000000000000000000000001";
        var sizedAvatarPath = Path.Combine(tempRoot, $"{uuid}-576.png");
        var legacyAvatarPath = Path.Combine(tempRoot, $"{uuid}.png");
        var otherAvatarPath = Path.Combine(tempRoot, "00000000000000000000000000000002-576.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(sizedAvatarPath, [1]);
        await File.WriteAllBytesAsync(legacyAvatarPath, [2]);
        await File.WriteAllBytesAsync(otherAvatarPath, [3]);
        var service = new AccountAvatarService(new HttpClient(), tempRoot);

        service.DeleteAvatar(uuid);

        Assert.False(File.Exists(sizedAvatarPath));
        Assert.False(File.Exists(legacyAvatarPath));
        Assert.True(File.Exists(otherAvatarPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static byte[] CreateSkinPng()
    {
        return CreateSkinPng(new Bgra(0x44, 0x88, 0xCC, byte.MaxValue), _ => { });
    }

    private static byte[] CreateSkinPng(Bgra face, Action<byte[]> configurePixels)
    {
        const int width = 64;
        const int height = 64;
        var pixels = new byte[width * height * 4];
        FillSkinArea(pixels, 8, 8, 8, 8, face);
        configurePixels(pixels);

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void FillSkinArea(byte[] pixels, int x, int y, int width, int height, Bgra color)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
                SetSkinPixel(pixels, column, row, color);
        }
    }

    private static void SetSkinPixel(byte[] pixels, int x, int y, Bgra color)
    {
        const int skinWidth = 64;
        var index = (y * skinWidth + x) * 4;
        pixels[index] = color.Blue;
        pixels[index + 1] = color.Green;
        pixels[index + 2] = color.Red;
        pixels[index + 3] = color.Alpha;
    }

    private static Bgra ReadAvatarPixel(string avatarPath, int x, int y)
    {
        using var stream = File.OpenRead(avatarPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        frame.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return new Bgra(pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private readonly record struct Bgra(byte Blue, byte Green, byte Red, byte Alpha);

    private sealed class SkinResponseHandler(byte[] skinPng) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(skinPng)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingSkinResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Skin download should not be requested.");
        }
    }
}

