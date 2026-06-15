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
        var avatarPath = Path.Combine(tempRoot, $"{uuid}-576.png");
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
        Assert.StartsWith(Path.Combine(tempRoot, $"{uuid}-576-"), avatarPath);
        Assert.True(File.Exists(avatarPath));
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
        const int width = 64;
        const int height = 64;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x44;
            pixels[i + 1] = 0x88;
            pixels[i + 2] = 0xCC;
            pixels[i + 3] = byte.MaxValue;
        }

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

