using Launcher.Infrastructure;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.Infrastructure.Accounts;

internal sealed class AccountAvatarService
{
    private const int AvatarSize = 576;

    private readonly HttpClient httpClient;
    private readonly string avatarDirectory;

    public AccountAvatarService(HttpClient httpClient, LauncherPathProvider pathProvider)
        : this(
            httpClient,
            Path.Combine(pathProvider.DefaultDataDirectory, "accounts", "microsoft", "avatars"))
    {
    }

    internal AccountAvatarService(HttpClient httpClient, string avatarDirectory)
    {
        this.httpClient = httpClient;
        this.avatarDirectory = avatarDirectory;
        Directory.CreateDirectory(this.avatarDirectory);
    }

    public async Task<string?> GetOrCreateAvatarSourceAsync(
        string uuid,
        string? skinUrl,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var cachedAvatarPath = GetLatestCachedAvatarPath(uuid);
        if (!forceRefresh && cachedAvatarPath is not null)
            return new Uri(cachedAvatarPath).AbsoluteUri;

        if (!forceRefresh)
            return GetFallbackAvatarSource(uuid, cacheBust: false);

        if (string.IsNullOrWhiteSpace(skinUrl))
            return GetFallbackAvatarSource(uuid, forceRefresh);

        try
        {
            var avatarPath = CreateAvatarPath(uuid);
            var skinBytes = await httpClient.GetByteArrayAsync(skinUrl, cancellationToken);
            var skin = LoadBitmap(skinBytes);
            var avatar = CreateAvatarBitmap(skin);
            SavePng(avatar, avatarPath);
            DeleteStaleAvatars(uuid, avatarPath);
            return new Uri(avatarPath).AbsoluteUri;
        }
        catch
        {
            return forceRefresh && cachedAvatarPath is null
                ? GetFallbackAvatarSource(uuid, forceRefresh)
                : cachedAvatarPath is not null && !forceRefresh
                    ? new Uri(cachedAvatarPath).AbsoluteUri
                    : GetFallbackAvatarSource(uuid, forceRefresh);
        }
    }

    public void DeleteAvatar(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid) || !Directory.Exists(avatarDirectory))
            return;

        foreach (var avatarPath in EnumerateAvatarFiles(uuid))
            TryDeleteFile(avatarPath);
    }

    private static BitmapSource LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateAvatarBitmap(BitmapSource skin)
    {
        var source = EnsureBgra32(skin);
        var face = ReadPixels(source, 8, 8, 8, 8);
        var overlay = source.PixelWidth >= 48 && source.PixelHeight >= 16
            ? ReadPixels(source, 40, 8, 8, 8)
            : null;

        var output = new byte[AvatarSize * AvatarSize * 4];
        for (var y = 0; y < AvatarSize; y++)
        {
            var sourceY = y * 8 / AvatarSize;
            for (var x = 0; x < AvatarSize; x++)
            {
                var sourceX = x * 8 / AvatarSize;
                var sourceIndex = (sourceY * 8 + sourceX) * 4;
                var outputIndex = (y * AvatarSize + x) * 4;

                output[outputIndex] = face[sourceIndex];
                output[outputIndex + 1] = face[sourceIndex + 1];
                output[outputIndex + 2] = face[sourceIndex + 2];
                output[outputIndex + 3] = face[sourceIndex + 3];

                if (overlay is not null)
                    BlendPixel(output, outputIndex, overlay, sourceIndex);
            }
        }

        var avatar = BitmapSource.Create(
            AvatarSize,
            AvatarSize,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            AvatarSize * 4);
        avatar.Freeze();
        return avatar;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] ReadPixels(BitmapSource source, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        source.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return pixels;
    }

    private static void BlendPixel(byte[] target, int targetIndex, byte[] overlay, int overlayIndex)
    {
        var overlayAlpha = overlay[overlayIndex + 3];
        if (overlayAlpha == 0)
            return;

        if (overlayAlpha == byte.MaxValue)
        {
            target[targetIndex] = overlay[overlayIndex];
            target[targetIndex + 1] = overlay[overlayIndex + 1];
            target[targetIndex + 2] = overlay[overlayIndex + 2];
            target[targetIndex + 3] = byte.MaxValue;
            return;
        }

        var inverseAlpha = byte.MaxValue - overlayAlpha;
        target[targetIndex] = (byte)((overlay[overlayIndex] * overlayAlpha + target[targetIndex] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 1] = (byte)((overlay[overlayIndex + 1] * overlayAlpha + target[targetIndex + 1] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 2] = (byte)((overlay[overlayIndex + 2] * overlayAlpha + target[targetIndex + 2] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 3] = byte.MaxValue;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private string CreateAvatarPath(string uuid)
    {
        return Path.Combine(avatarDirectory, $"{uuid}-{AvatarSize}-{Guid.NewGuid():N}.png");
    }

    private string? GetLatestCachedAvatarPath(string uuid)
    {
        return EnumerateAvatarFiles(uuid)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private IEnumerable<string> EnumerateAvatarFiles(string uuid)
    {
        if (!Directory.Exists(avatarDirectory))
            return [];

        return Directory.EnumerateFiles(avatarDirectory, $"{uuid}*.png");
    }

    private void DeleteStaleAvatars(string uuid, string currentAvatarPath)
    {
        foreach (var avatarPath in EnumerateAvatarFiles(uuid))
        {
            if (string.Equals(avatarPath, currentAvatarPath, StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteFile(avatarPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string GetFallbackAvatarSource(string uuid, bool cacheBust)
    {
        var uri = $"https://crafatar.com/avatars/{uuid}?size={AvatarSize}&overlay";
        return cacheBust
            ? $"{uri}&t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : uri;
    }
}
