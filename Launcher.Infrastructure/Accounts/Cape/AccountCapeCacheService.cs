using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Launcher.Infrastructure.Accounts;

internal sealed class AccountCapeCacheService
{
    private const string CapeCacheVersion = "v1";

    private readonly HttpClient httpClient;
    private readonly string capeDirectory;

    public AccountCapeCacheService(HttpClient httpClient, LauncherPathProvider pathProvider)
        : this(
            httpClient,
            Path.Combine(pathProvider.DefaultDataDirectory, "accounts", "microsoft", "capes"))
    {
    }

    internal AccountCapeCacheService(HttpClient httpClient, string capeDirectory)
    {
        this.httpClient = httpClient;
        this.capeDirectory = capeDirectory;
        Directory.CreateDirectory(this.capeDirectory);
    }

    public async Task<string?> GetOrCreateCapeSourceAsync(
        string accountId,
        string? capeId,
        string? capeUrl,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(capeUrl))
            return null;

        if (TryResolveExistingLocalCape(capeUrl) is { } localCapeSource)
            return localCapeSource;

        var cacheKey = string.IsNullOrWhiteSpace(capeId) ? capeUrl : capeId;
        var cachedCapePath = GetLatestCachedCapePath(accountId, cacheKey);
        if (!forceRefresh && cachedCapePath is not null)
            return new Uri(cachedCapePath).AbsoluteUri;

        try
        {
            var capeBytes = await httpClient.GetByteArrayAsync(capeUrl, cancellationToken);
            var hash = ComputeHash(capeBytes);
            var capePath = CreateCapePath(accountId, cacheKey, hash);
            if (!File.Exists(capePath) || forceRefresh)
                await File.WriteAllBytesAsync(capePath, capeBytes, cancellationToken);
            DeleteStaleCapes(accountId, cacheKey, capePath);
            return new Uri(capePath).AbsoluteUri;
        }
        catch
        {
            return cachedCapePath is null ? capeUrl : new Uri(cachedCapePath).AbsoluteUri;
        }
    }

    private string CreateCapePath(string accountId, string cacheKey, string contentHash)
    {
        var accountCapeDirectory = GetAccountCapeDirectory(accountId);
        Directory.CreateDirectory(accountCapeDirectory);
        var safeKey = SanitizePathSegment(cacheKey);
        if (safeKey.Length > 48)
            safeKey = safeKey[..48];
        var safeHash = contentHash.Length > 24 ? contentHash[..24] : contentHash;
        return Path.Combine(accountCapeDirectory, $"{CapeCacheVersion}-{safeKey}-{safeHash}.png");
    }

    private string? GetLatestCachedCapePath(string accountId, string cacheKey)
    {
        var accountCapeDirectory = GetAccountCapeDirectory(accountId);
        if (!Directory.Exists(accountCapeDirectory))
            return null;

        var safeKey = SanitizePathSegment(cacheKey);
        if (safeKey.Length > 48)
            safeKey = safeKey[..48];

        return Directory.EnumerateFiles(accountCapeDirectory, $"{CapeCacheVersion}-{safeKey}-*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private void DeleteStaleCapes(string accountId, string cacheKey, string currentCapePath)
    {
        var accountCapeDirectory = GetAccountCapeDirectory(accountId);
        if (!Directory.Exists(accountCapeDirectory))
            return;

        var safeKey = SanitizePathSegment(cacheKey);
        if (safeKey.Length > 48)
            safeKey = safeKey[..48];

        foreach (var capePath in Directory.EnumerateFiles(accountCapeDirectory, $"{CapeCacheVersion}-{safeKey}-*.png"))
        {
            if (string.Equals(capePath, currentCapePath, StringComparison.OrdinalIgnoreCase))
                continue;

            TryDeleteFile(capePath);
        }
    }

    private string GetAccountCapeDirectory(string accountId)
    {
        return Path.Combine(capeDirectory, SanitizePathSegment(accountId));
    }

    private static string? TryResolveExistingLocalCape(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsFile)
            return null;

        return File.Exists(uri.LocalPath) ? uri.AbsoluteUri : null;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }

    private static string ComputeHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
