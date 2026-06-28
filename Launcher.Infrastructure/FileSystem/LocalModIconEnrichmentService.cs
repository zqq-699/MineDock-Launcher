using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalModIconEnrichmentService : ILocalModIconEnrichmentService
{
    private const string ModrinthBaseUrl = "https://api.modrinth.com/v2";
    private const string CurseForgeBaseUrl = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;
    private const long MaxIconBytes = 1024L * 1024L;
    private const long MaxCacheBytes = 50L * 1024L * 1024L;
    private const long TargetCacheBytes = 40L * 1024L * 1024L;
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan UnusedExpiration = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient httpClient;
    private readonly LauncherPathProvider pathProvider;
    private readonly ICurseForgeApiKeyResolver curseForgeApiKeyResolver;
    private readonly ILogger<LocalModIconEnrichmentService> logger;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private readonly string cacheDirectory;
    private readonly string indexPath;
    private bool cleanupCompleted;

    public LocalModIconEnrichmentService(
        LauncherPathProvider? pathProvider = null,
        HttpClient? httpClient = null,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null,
        ILogger<LocalModIconEnrichmentService>? logger = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.httpClient = httpClient ?? new HttpClient();
        this.curseForgeApiKeyResolver = curseForgeApiKeyResolver
            ?? new CurseForgeApiKeyResolver(this.pathProvider);
        this.logger = logger ?? NullLogger<LocalModIconEnrichmentService>.Instance;
        cacheDirectory = Path.Combine(this.pathProvider.DefaultDataDirectory, "cache", "mods", "remote-icons");
        indexPath = Path.Combine(cacheDirectory, "index.json");
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default,
        IProgress<IReadOnlyDictionary<string, string>>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var candidates = mods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .Where(mod => !string.IsNullOrWhiteSpace(mod.FullPath) && File.Exists(mod.FullPath))
            .GroupBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Remote local mod icon enrichment started. CandidateCount={CandidateCount}",
            candidates.Count);

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staleResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<ModIconLookupCandidate>();

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        RemoteIconCacheIndex index;
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);

            foreach (var mod in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lookup = await CreateLookupCandidateAsync(mod, cancellationToken).ConfigureAwait(false);
                if (lookup is null)
                    continue;

                var cachedIcon = TryGetCachedIcon(
                    index,
                    lookup.Sha1Alias,
                    now,
                    allowStale: true,
                    updateLastUsed: true,
                    out var isStale);
                if (cachedIcon is not null)
                {
                    CacheFileAlias(index, lookup);
                    if (isStale)
                    {
                        staleResults[mod.FullPath] = cachedIcon;
                        unresolved.Add(lookup);
                    }
                    else
                    {
                        result[mod.FullPath] = cachedIcon;
                    }
                }
                else
                {
                    unresolved.Add(lookup);
                }
            }

            if (result.Count > 0 || staleResults.Count > 0)
            {
                await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
                ReportProgress(progress, result);
                ReportProgress(progress, staleResults);
                logger.LogInformation(
                    "Remote local mod icon cache resolved icons. FreshCount={FreshCount} StaleCount={StaleCount}",
                    result.Count,
                    staleResults.Count);
            }
        }
        finally
        {
            cacheLock.Release();
        }

        if (unresolved.Count > 0)
        {
            try
            {
                var resolved = await ResolveRemoteIconsAsync(unresolved, cancellationToken, progress).ConfigureAwait(false);
                foreach (var pair in resolved)
                    result[pair.Key] = pair.Value;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to resolve remote local mod icons. Cached icons will still be used.");
            }
        }

        foreach (var pair in staleResults)
            result.TryAdd(pair.Key, pair.Value);

        await CleanupCacheOnceAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Remote local mod icon enrichment completed. CandidateCount={CandidateCount} ResolvedCount={ResolvedCount}",
            candidates.Count,
            result.Count);
        return result;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var candidates = mods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .Where(mod => !string.IsNullOrWhiteSpace(mod.FullPath) && File.Exists(mod.FullPath))
            .GroupBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            foreach (var mod in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileAlias = TryCreateFileAlias(mod.FullPath);
                if (fileAlias is null
                    || !index.FileAliases.TryGetValue(fileAlias, out var entryKey))
                    continue;

                var cachedIcon = TryGetCachedIconByEntryKey(
                    index,
                    entryKey,
                    now,
                    allowStale: true,
                    updateLastUsed: false,
                    out _);
                if (cachedIcon is not null)
                    result[mod.FullPath] = cachedIcon;
            }
        }
        finally
        {
            cacheLock.Release();
        }

        logger.LogInformation(
            "Remote local mod icon cache checked. CandidateCount={CandidateCount} HitCount={HitCount}",
            candidates.Count,
            result.Count);
        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveRemoteIconsAsync(
        IReadOnlyList<ModIconLookupCandidate> candidates,
        CancellationToken cancellationToken,
        IProgress<IReadOnlyDictionary<string, string>>? progress)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = candidates.ToDictionary(candidate => candidate.Sha1, StringComparer.OrdinalIgnoreCase);

        var modrinthIcons = await ResolveModrinthIconsAsync(candidates, cancellationToken).ConfigureAwait(false);
        foreach (var (sha1, icon) in modrinthIcons)
        {
            if (!unresolved.TryGetValue(sha1, out var candidate))
                continue;

            var iconSource = await TryCacheRemoteIconAsync(candidate, icon, cancellationToken).ConfigureAwait(false);
            if (iconSource is null)
                continue;

            result[candidate.FullPath] = iconSource;
            ReportProgress(progress, candidate.FullPath, iconSource);
            unresolved.Remove(sha1);
        }

        if (unresolved.Count > 0)
        {
            var curseForgeIcons = await ResolveCurseForgeIconsAsync(unresolved.Values.ToList(), cancellationToken)
                .ConfigureAwait(false);
            foreach (var (sha1, icon) in curseForgeIcons)
            {
                if (!unresolved.TryGetValue(sha1, out var candidate))
                    continue;

                var iconSource = await TryCacheRemoteIconAsync(candidate, icon, cancellationToken).ConfigureAwait(false);
                if (iconSource is null)
                    continue;

                result[candidate.FullPath] = iconSource;
                ReportProgress(progress, candidate.FullPath, iconSource);
            }
        }

        return result;
    }

    private static void ReportProgress(
        IProgress<IReadOnlyDictionary<string, string>>? progress,
        IReadOnlyDictionary<string, string> icons)
    {
        if (progress is null || icons.Count == 0)
            return;

        progress.Report(new Dictionary<string, string>(icons, StringComparer.OrdinalIgnoreCase));
    }

    private static void ReportProgress(
        IProgress<IReadOnlyDictionary<string, string>>? progress,
        string fullPath,
        string iconSource)
    {
        if (progress is null || string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(iconSource))
            return;

        progress.Report(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [fullPath] = iconSource
        });
    }

    private async Task<string?> TryCacheRemoteIconAsync(
        ModIconLookupCandidate lookup,
        RemoteIconCandidate icon,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CacheRemoteIconAsync(lookup, icon, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to cache remote local mod icon. Source={Source} ProjectId={ProjectId}",
                icon.Source,
                icon.ProjectId);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, RemoteIconCandidate>> ResolveModrinthIconsAsync(
        IReadOnlyList<ModIconLookupCandidate> candidates,
        CancellationToken cancellationToken)
    {
        try
        {
            var hashes = candidates.Select(candidate => candidate.Sha1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            using var response = await httpClient.PostAsJsonAsync(
                    $"{ModrinthBaseUrl}/version_files",
                    new ModrinthVersionFilesRequest(hashes, "sha1"),
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Modrinth local mod icon lookup was rejected. StatusCode={StatusCode}",
                    response.StatusCode);
                return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
            }

            var versions = await response.Content.ReadFromJsonAsync<Dictionary<string, ModrinthVersionFileMatch>>(
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? new Dictionary<string, ModrinthVersionFileMatch>(StringComparer.OrdinalIgnoreCase);
            var projectIds = versions.Values
                .Select(match => match.ProjectId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (projectIds.Length == 0)
                return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);

            var projectsParameter = Uri.EscapeDataString(JsonSerializer.Serialize(projectIds, JsonOptions));
            var projects = await httpClient.GetFromJsonAsync<List<ModrinthProject>>(
                    $"{ModrinthBaseUrl}/projects?ids={projectsParameter}",
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? [];
            var projectsById = projects
                .Where(project => !string.IsNullOrWhiteSpace(project.Id))
                .ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sha1, version) in versions)
            {
                if (string.IsNullOrWhiteSpace(version.ProjectId)
                    || !projectsById.TryGetValue(version.ProjectId, out var project)
                    || string.IsNullOrWhiteSpace(project.IconUrl))
                {
                    continue;
                }

                result[sha1] = new RemoteIconCandidate("modrinth", version.ProjectId, project.IconUrl);
            }

            logger.LogInformation(
                "Modrinth resolved remote local mod icons. RequestedCount={RequestedCount} ResolvedCount={ResolvedCount}",
                hashes.Length,
                result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to resolve remote local mod icons from Modrinth.");
            return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyDictionary<string, RemoteIconCandidate>> ResolveCurseForgeIconsAsync(
        IReadOnlyList<ModIconLookupCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var apiKey = await curseForgeApiKeyResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogInformation("Skipping CurseForge local mod icon lookup because API key is not configured.");
            return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var fingerprints = candidates.Select(candidate => candidate.CurseForgeFingerprint).Distinct().ToArray();
            using var fingerprintRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{CurseForgeBaseUrl}/fingerprints/{MinecraftGameId}")
            {
                Content = JsonContent.Create(new CurseForgeFingerprintRequest(fingerprints), options: JsonOptions)
            };
            fingerprintRequest.Headers.Add("x-api-key", apiKey);

            using var fingerprintResponse = await httpClient.SendAsync(fingerprintRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!fingerprintResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CurseForge local mod fingerprint lookup was rejected. StatusCode={StatusCode}",
                    fingerprintResponse.StatusCode);
                return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
            }

            await using var fingerprintStream = await fingerprintResponse.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var projectByFingerprint = await ParseCurseForgeFingerprintMatchesAsync(fingerprintStream, cancellationToken)
                .ConfigureAwait(false);
            if (projectByFingerprint.Count == 0)
                return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);

            var projectIds = projectByFingerprint.Values.Distinct().ToArray();
            using var modsRequest = new HttpRequestMessage(HttpMethod.Post, $"{CurseForgeBaseUrl}/mods")
            {
                Content = JsonContent.Create(new CurseForgeModsRequest(projectIds), options: JsonOptions)
            };
            modsRequest.Headers.Add("x-api-key", apiKey);

            using var modsResponse = await httpClient.SendAsync(modsRequest, cancellationToken).ConfigureAwait(false);
            if (!modsResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CurseForge local mod icon metadata lookup was rejected. StatusCode={StatusCode}",
                    modsResponse.StatusCode);
                return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
            }

            var mods = await modsResponse.Content.ReadFromJsonAsync<CurseForgeModsResponse>(
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? new CurseForgeModsResponse();
            var modsById = mods.Data.ToDictionary(mod => mod.Id);
            var candidatesByFingerprint = candidates.ToDictionary(
                candidate => candidate.CurseForgeFingerprint,
                candidate => candidate,
                EqualityComparer<long>.Default);
            var result = new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fingerprint, projectId) in projectByFingerprint)
            {
                if (!candidatesByFingerprint.TryGetValue(fingerprint, out var candidate)
                    || !modsById.TryGetValue(projectId, out var mod))
                {
                    continue;
                }

                var iconUrl = string.IsNullOrWhiteSpace(mod.Logo?.Url)
                    ? mod.Logo?.ThumbnailUrl
                    : mod.Logo.Url;
                if (string.IsNullOrWhiteSpace(iconUrl))
                    continue;

                result[candidate.Sha1] = new RemoteIconCandidate("curseforge", projectId.ToString(), iconUrl);
            }

            logger.LogInformation(
                "CurseForge resolved remote local mod icons. RequestedCount={RequestedCount} ResolvedCount={ResolvedCount}",
                candidates.Count,
                result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to resolve remote local mod icons from CurseForge.");
            return new Dictionary<string, RemoteIconCandidate>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<string?> CacheRemoteIconAsync(
        ModIconLookupCandidate lookup,
        RemoteIconCandidate icon,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(icon.IconUrl, UriKind.Absolute, out var iconUri))
            return null;

        byte[] imageBytes;
        try
        {
            using var response = await httpClient.GetAsync(iconUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Remote local mod icon download was rejected. Source={Source} ProjectId={ProjectId} StatusCode={StatusCode}",
                    icon.Source,
                    icon.ProjectId,
                    response.StatusCode);
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaxIconBytes)
            {
                logger.LogWarning(
                    "Remote local mod icon is too large. Source={Source} ProjectId={ProjectId} SizeBytes={SizeBytes}",
                    icon.Source,
                    icon.ProjectId,
                    contentLength);
                return null;
            }

            await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            imageBytes = await ReadLimitedAsync(remoteStream, MaxIconBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to download remote local mod icon. Source={Source} ProjectId={ProjectId}",
                icon.Source,
                icon.ProjectId);
            return null;
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var entryKey = $"{icon.Source}:{icon.ProjectId}";
            var cachePath = GetIconCachePath(entryKey);

            try
            {
                await using var input = new MemoryStream(imageBytes);
                SaveRemoteIconAsPng(input, cachePath);
            }
            catch (Exception exception) when (
                exception is NotSupportedException
                or InvalidDataException
                or ArgumentException
                or IOException)
            {
                logger.LogWarning(
                    exception,
                    "Remote local mod icon was invalid. Source={Source} ProjectId={ProjectId}",
                    icon.Source,
                    icon.ProjectId);
                return null;
            }

            var sizeBytes = new FileInfo(cachePath).Length;
            index.Entries[entryKey] = new RemoteIconCacheEntry
            {
                Source = icon.Source,
                ProjectId = icon.ProjectId,
                IconUrl = icon.IconUrl,
                FileName = Path.GetFileName(cachePath),
                CachedAt = now,
                LastUsedAt = now,
                SizeBytes = sizeBytes
            };
            index.Aliases[lookup.Sha1Alias] = entryKey;
            index.FileAliases[lookup.FileAlias] = entryKey;
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Remote local mod icon cached. Source={Source} ProjectId={ProjectId} SizeBytes={SizeBytes}",
                icon.Source,
                icon.ProjectId,
                sizeBytes);
            return new Uri(cachePath).AbsoluteUri;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private string? TryGetCachedIcon(
        RemoteIconCacheIndex index,
        string alias,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        if (!index.Aliases.TryGetValue(alias, out var entryKey)
            || !index.Entries.TryGetValue(entryKey, out var entry))
        {
            return null;
        }

        return TryGetCachedIconCore(entry, now, allowStale, updateLastUsed, out isStale);
    }

    private string? TryGetCachedIconByEntryKey(
        RemoteIconCacheIndex index,
        string entryKey,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        if (!index.Entries.TryGetValue(entryKey, out var entry))
            return null;

        return TryGetCachedIconCore(entry, now, allowStale, updateLastUsed, out isStale);
    }

    private string? TryGetCachedIconCore(
        RemoteIconCacheEntry entry,
        DateTimeOffset now,
        bool allowStale,
        bool updateLastUsed,
        out bool isStale)
    {
        isStale = false;
        var path = Path.Combine(cacheDirectory, entry.FileName);
        if (!File.Exists(path))
            return null;

        if (updateLastUsed)
            entry.LastUsedAt = now;
        isStale = now - entry.CachedAt > RefreshAfter;
        return isStale && !allowStale ? null : new Uri(path).AbsoluteUri;
    }

    private async Task CleanupCacheOnceAsync(CancellationToken cancellationToken)
    {
        if (cleanupCompleted)
            return;

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cleanupCompleted)
                return;

            Directory.CreateDirectory(cacheDirectory);
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var removed = 0;

            foreach (var pair in index.Entries.ToArray())
            {
                var path = Path.Combine(cacheDirectory, pair.Value.FileName);
                if (!File.Exists(path) || now - pair.Value.LastUsedAt > UnusedExpiration)
                {
                    DeleteFileIfExists(path);
                    index.Entries.Remove(pair.Key);
                    removed++;
                }
                else
                {
                    pair.Value.SizeBytes = new FileInfo(path).Length;
                }
            }

            var totalBytes = index.Entries.Values.Sum(entry => entry.SizeBytes);
            if (totalBytes > MaxCacheBytes)
            {
                foreach (var pair in index.Entries.OrderBy(pair => pair.Value.LastUsedAt).ToArray())
                {
                    if (totalBytes <= TargetCacheBytes)
                        break;

                    var path = Path.Combine(cacheDirectory, pair.Value.FileName);
                    DeleteFileIfExists(path);
                    totalBytes -= pair.Value.SizeBytes;
                    index.Entries.Remove(pair.Key);
                    removed++;
                }
            }

            foreach (var alias in index.Aliases
                         .Where(pair => !index.Entries.ContainsKey(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                index.Aliases.Remove(alias);
            }

            foreach (var alias in index.FileAliases
                         .Where(pair => !index.Entries.ContainsKey(pair.Value))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                index.FileAliases.Remove(alias);
            }

            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
            cleanupCompleted = true;
            logger.LogInformation(
                "Remote local mod icon cache cleanup completed. RemovedCount={RemovedCount} TotalBytes={TotalBytes}",
                removed,
                index.Entries.Values.Sum(entry => entry.SizeBytes));
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<ModIconLookupCandidate?> CreateLookupCandidateAsync(
        LocalMod mod,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                mod.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                useAsync: true);
            using var sha1 = SHA1.Create();
            using var fingerprintBytes = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                sha1.TransformBlock(buffer, 0, read, null, 0);
                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];
                    if (value is not (0x09 or 0x0a or 0x0d or 0x20))
                        fingerprintBytes.WriteByte(value);
                }
            }

            sha1.TransformFinalBlock([], 0, 0);
            var sha1Text = Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
            var fileAlias = TryCreateFileAlias(mod.FullPath);
            if (fileAlias is null)
                return null;

            return new ModIconLookupCandidate(
                mod.FullPath,
                sha1Text,
                fileAlias,
                ComputeCurseForgeMurmurHash2(fingerprintBytes.GetBuffer().AsSpan(0, (int)fingerprintBytes.Length)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or CryptographicException)
        {
            logger.LogWarning(
                exception,
                "Failed to hash local mod for remote icon lookup. FileName={FileName}",
                mod.FileName);
            return null;
        }
    }

    private async Task<RemoteIconCacheIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
            return new RemoteIconCacheIndex();

        try
        {
            await using var stream = File.OpenRead(indexPath);
            return await JsonSerializer.DeserializeAsync<RemoteIconCacheIndex>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                ?? new RemoteIconCacheIndex();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Failed to parse remote local mod icon cache index.");
            return new RemoteIconCacheIndex();
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to read remote local mod icon cache index.");
            return new RemoteIconCacheIndex();
        }
    }

    private async Task SaveIndexAsync(RemoteIconCacheIndex index, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        var tempPath = indexPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, index, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, indexPath, overwrite: true);
    }

    private void CacheFileAlias(RemoteIconCacheIndex index, ModIconLookupCandidate lookup)
    {
        if (index.Aliases.TryGetValue(lookup.Sha1Alias, out var entryKey))
            index.FileAliases[lookup.FileAlias] = entryKey;
    }

    private static string? TryCreateFileAlias(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                return null;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"file:{Path.GetFullPath(fileInfo.FullName)}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return buffer.ToArray();

            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("Remote local mod icon exceeds the maximum allowed size.");

            buffer.Write(chunk, 0, read);
        }
    }

    private void SaveRemoteIconAsPng(Stream source, string path)
    {
        var decoder = BitmapDecoder.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Remote local mod icon contains no frames.");
        frame.Freeze();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame, null, null, null));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private string GetIconCachePath(string entryKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entryKey))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.png");
    }

    private static async Task<Dictionary<long, long>> ParseCurseForgeFingerprintMatchesAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, long>();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("exactMatches", out var exactMatches)
            || exactMatches.ValueKind is not JsonValueKind.Array)
        {
            return result;
        }

        foreach (var match in exactMatches.EnumerateArray())
        {
            var projectId = TryReadLong(match, "id")
                            ?? TryReadLong(match, "modId")
                            ?? TryReadLong(match, "projectId");
            JsonElement? file = match.TryGetProperty("file", out var fileElement) ? fileElement : null;
            if (projectId is null && file is not null)
                projectId = TryReadLong(file.Value, "modId") ?? TryReadLong(file.Value, "projectId");

            var fingerprint = TryReadLong(match, "fileFingerprint")
                              ?? TryReadLong(match, "fingerprint");
            if (fingerprint is null && file is not null)
            {
                fingerprint = TryReadLong(file.Value, "fileFingerprint")
                              ?? TryReadLong(file.Value, "fingerprint")
                              ?? TryReadFirstFingerprint(file.Value);
            }

            if (projectId is not null && fingerprint is not null)
                result[fingerprint.Value] = projectId.Value;
        }

        return result;
    }

    private static long? TryReadFirstFingerprint(JsonElement element)
    {
        if (!element.TryGetProperty("fingerprints", out var fingerprints)
            || fingerprints.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var fingerprint in fingerprints.EnumerateArray())
        {
            var value = fingerprint.ValueKind is JsonValueKind.Object
                ? TryReadLong(fingerprint, "value")
                : TryReadLong(fingerprint);
            if (value is not null)
                return value;
        }

        return null;
    }

    private static long? TryReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? TryReadLong(property)
            : null;
    }

    private static long? TryReadLong(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static long ComputeCurseForgeMurmurHash2(ReadOnlySpan<byte> data)
    {
        const uint seed = 1;
        const uint m = 0x5bd1e995;
        const int r = 24;

        var length = data.Length;
        var hash = seed ^ (uint)length;
        var current = data;
        while (current.Length >= 4)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(current);
            value *= m;
            value ^= value >> r;
            value *= m;

            hash *= m;
            hash ^= value;
            current = current[4..];
        }

        switch (current.Length)
        {
            case 3:
                hash ^= (uint)current[2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)current[1] << 8;
                goto case 1;
            case 1:
                hash ^= current[0];
                hash *= m;
                break;
        }

        hash ^= hash >> 13;
        hash *= m;
        hash ^= hash >> 15;
        return hash;
    }

    private sealed record ModIconLookupCandidate(string FullPath, string Sha1, string FileAlias, long CurseForgeFingerprint)
    {
        public string Sha1Alias => $"sha1:{Sha1}";
    }

    private sealed record RemoteIconCandidate(string Source, string ProjectId, string IconUrl);

    private sealed record ModrinthVersionFilesRequest(
        [property: JsonPropertyName("hashes")] IReadOnlyList<string> Hashes,
        [property: JsonPropertyName("algorithm")] string Algorithm);

    private sealed class ModrinthVersionFileMatch
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; init; } = string.Empty;
    }

    private sealed class ModrinthProject
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; init; }
    }

    private sealed record CurseForgeFingerprintRequest(
        [property: JsonPropertyName("fingerprints")] IReadOnlyList<long> Fingerprints);

    private sealed record CurseForgeModsRequest(
        [property: JsonPropertyName("modIds")] IReadOnlyList<long> ModIds);

    private sealed class CurseForgeModsResponse
    {
        [JsonPropertyName("data")]
        public List<CurseForgeMod> Data { get; init; } = [];
    }

    private sealed class CurseForgeMod
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("logo")]
        public CurseForgeModLogo? Logo { get; init; }
    }

    private sealed class CurseForgeModLogo
    {
        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }

    private sealed class RemoteIconCacheIndex
    {
        public Dictionary<string, RemoteIconCacheEntry> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Aliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> FileAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RemoteIconCacheEntry
    {
        public string Source { get; init; } = string.Empty;

        public string ProjectId { get; init; } = string.Empty;

        public string IconUrl { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public DateTimeOffset CachedAt { get; init; }

        public DateTimeOffset LastUsedAt { get; set; }

        public long SizeBytes { get; set; }
    }

}
