using System.IO;
using System.Net.Http;

namespace Launcher.Infrastructure.Accounts;

internal sealed class AccountSkinCacheService
{
    private const string SkinCacheVersion = "v1";

    private readonly HttpClient httpClient;
    private readonly string skinDirectory;

    public AccountSkinCacheService(HttpClient httpClient, LauncherPathProvider pathProvider)
        : this(
            httpClient,
            Path.Combine(pathProvider.DefaultDataDirectory, "accounts", "microsoft", "skins"))
    {
    }

    internal AccountSkinCacheService(HttpClient httpClient, string skinDirectory)
    {
        this.httpClient = httpClient;
        this.skinDirectory = skinDirectory;
        Directory.CreateDirectory(this.skinDirectory);
    }

    public async Task<string?> GetOrCreateSkinSourceAsync(
        string uuid,
        string? skinUrl,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var cachedSkinPath = GetLatestCachedSkinPath(uuid);
        if (!forceRefresh)
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;

        if (string.IsNullOrWhiteSpace(skinUrl))
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;

        try
        {
            var skinBytes = await httpClient.GetByteArrayAsync(skinUrl, cancellationToken);
            var skinPath = CreateSkinPath(uuid);
            await File.WriteAllBytesAsync(skinPath, skinBytes, cancellationToken);
            DeleteStaleSkins(uuid, skinPath);
            return new Uri(skinPath).AbsoluteUri;
        }
        catch
        {
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;
        }
    }

    public async Task<string?> StoreUploadedSkinAsync(
        string uuid,
        string skinFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(skinFilePath) || !File.Exists(skinFilePath))
            return GetLatestCachedSkinPath(uuid) is { } cachedSkinPath
                ? new Uri(cachedSkinPath).AbsoluteUri
                : null;

        var skinPath = CreateSkinPath(uuid);
        await using (var source = File.OpenRead(skinFilePath))
        await using (var target = File.Create(skinPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        DeleteStaleSkins(uuid, skinPath);
        return new Uri(skinPath).AbsoluteUri;
    }

    private string CreateSkinPath(string uuid)
    {
        Directory.CreateDirectory(skinDirectory);
        return Path.Combine(skinDirectory, $"{uuid}-{SkinCacheVersion}-{Guid.NewGuid():N}.png");
    }

    private string? GetLatestCachedSkinPath(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid) || !Directory.Exists(skinDirectory))
            return null;

        return Directory.EnumerateFiles(skinDirectory, $"{uuid}-{SkinCacheVersion}-*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private void DeleteStaleSkins(string uuid, string currentSkinPath)
    {
        if (string.IsNullOrWhiteSpace(uuid) || !Directory.Exists(skinDirectory))
            return;

        foreach (var skinPath in Directory.EnumerateFiles(skinDirectory, $"{uuid}-*.png"))
        {
            if (string.Equals(skinPath, currentSkinPath, StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteFile(skinPath);
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
}
