using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Mods;

public sealed class LocalModIconEnrichmentServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ResolveMissingIconSourcesAsyncResolvesModrinthIconAndCachesIt()
    {
        var jarPath = await WriteModFileAsync("sodium.jar", "modrinth-content");
        var sha1 = await ComputeSha1Async(jarPath);
        var iconBytes = CreatePngBytes(Colors.CornflowerBlue);
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = $$"""
            {
              "{{sha1}}": { "project_id": "modrinth-project" }
            }
            """,
            ModrinthProjectsResponse = """
            [
              { "id": "modrinth-project", "icon_url": "https://cdn.example/sodium.png" }
            ]
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/sodium.png"] = iconBytes
            }
        };
        var service = CreateService(handler);

        var result = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        var iconSource = Assert.Single(result).Value;
        Assert.True(File.Exists(new Uri(iconSource).LocalPath));
        Assert.Equal(1, handler.ModrinthVersionFileRequestCount);
        Assert.Equal(1, handler.ModrinthProjectRequestCount);
        Assert.Equal(1, handler.IconDownloadRequestCount);

        var cacheHitHandler = new ModIconLookupHandler
        {
            ThrowOnUnexpectedRequest = true
        };
        var cachedService = CreateService(cacheHitHandler);
        var progressReports = new List<IReadOnlyDictionary<string, string>>();
        var progress = new CapturingProgress(progressReports);

        var cachedResult = await cachedService.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)], progress: progress);

        Assert.Equal(iconSource, Assert.Single(cachedResult).Value);
        Assert.Equal(iconSource, Assert.Single(Assert.Single(progressReports)).Value);
        Assert.Equal(0, cacheHitHandler.TotalRequestCount);
    }

    [Fact]
    public async Task ResolveCachedIconSourcesAsyncReturnsCachedIconWithoutRemoteRequests()
    {
        var jarPath = await WriteModFileAsync("cached-only.jar", "cached-only-content");
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Directory.CreateDirectory(cacheDirectory);
        var cachedIconPath = Path.Combine(cacheDirectory, "cached-only.png");
        await File.WriteAllBytesAsync(cachedIconPath, CreatePngBytes(Colors.MediumPurple));
        var timestamp = DateTimeOffset.UtcNow.AddDays(-7);
        await File.WriteAllTextAsync(
            Path.Combine(cacheDirectory, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    entries = new Dictionary<string, object>
                    {
                        ["modrinth:cached-only-project"] = new
                        {
                            source = "modrinth",
                            projectId = "cached-only-project",
                            iconUrl = "https://cdn.example/cached-only.png",
                            fileName = "cached-only.png",
                            cachedAt = timestamp,
                            lastUsedAt = timestamp,
                            sizeBytes = new FileInfo(cachedIconPath).Length
                        }
                    },
                    aliases = new Dictionary<string, string>(),
                    fileAliases = new Dictionary<string, string>
                    {
                        [CreateFileAlias(jarPath)] = "modrinth:cached-only-project"
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        var handler = new ModIconLookupHandler
        {
            ThrowOnUnexpectedRequest = true
        };
        var service = CreateService(handler);

        var result = await service.ResolveCachedIconSourcesAsync([CreateLocalMod(jarPath)]);

        var iconSource = Assert.Single(result).Value;
        Assert.Equal(cachedIconPath, new Uri(iconSource).LocalPath);
        Assert.Equal(0, handler.TotalRequestCount);
    }

    [Fact]
    public async Task ResolveCachedIconSourcesAsyncIgnoresSha1OnlyCacheWithoutRemoteRequests()
    {
        var jarPath = await WriteModFileAsync("sha1-only.jar", "sha1-only-content");
        var sha1 = await ComputeSha1Async(jarPath);
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Directory.CreateDirectory(cacheDirectory);
        var cachedIconPath = Path.Combine(cacheDirectory, "sha1-only.png");
        await File.WriteAllBytesAsync(cachedIconPath, CreatePngBytes(Colors.MediumPurple));
        var timestamp = DateTimeOffset.UtcNow.AddDays(-7);
        await File.WriteAllTextAsync(
            Path.Combine(cacheDirectory, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    entries = new Dictionary<string, object>
                    {
                        ["modrinth:sha1-only-project"] = new
                        {
                            source = "modrinth",
                            projectId = "sha1-only-project",
                            iconUrl = "https://cdn.example/sha1-only.png",
                            fileName = "sha1-only.png",
                            cachedAt = timestamp,
                            lastUsedAt = timestamp,
                            sizeBytes = new FileInfo(cachedIconPath).Length
                        }
                    },
                    aliases = new Dictionary<string, string>
                    {
                        [$"sha1:{sha1}"] = "modrinth:sha1-only-project"
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        var handler = new ModIconLookupHandler
        {
            ThrowOnUnexpectedRequest = true
        };
        var service = CreateService(handler);

        var result = await service.ResolveCachedIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.Empty(result);
        Assert.Equal(0, handler.TotalRequestCount);
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncWritesFileAliasForFutureFastCacheHits()
    {
        var jarPath = await WriteModFileAsync("future-fast-cache.jar", "future-fast-cache-content");
        var sha1 = await ComputeSha1Async(jarPath);
        var iconBytes = CreatePngBytes(Colors.CornflowerBlue);
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = $$"""
            {
              "{{sha1}}": { "project_id": "future-fast-cache-project" }
            }
            """,
            ModrinthProjectsResponse = """
            [
              { "id": "future-fast-cache-project", "icon_url": "https://cdn.example/future-fast-cache.png" }
            ]
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/future-fast-cache.png"] = iconBytes
            }
        };
        var service = CreateService(handler);

        var result = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.NotEmpty(result);
        var indexJson = await File.ReadAllTextAsync(Path.Combine(
            TempRoot,
            LauncherApplicationIdentity.StorageDirectoryName,
            "cache",
            "mods",
            "remote-icons",
            "index.json"));
        using var document = JsonDocument.Parse(indexJson);
        var fileAliases = document.RootElement.GetProperty("fileAliases");
        Assert.True(fileAliases.TryGetProperty(CreateFileAlias(jarPath), out var entryKey));
        Assert.Equal("modrinth:future-fast-cache-project", entryKey.GetString());
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncFallsBackToCurseForgeWhenModrinthMisses()
    {
        var jarPath = await WriteModFileAsync("fallback.jar", "curseforge-content");
        var iconBytes = CreatePngBytes(Colors.DarkOrange);
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = "{}",
            CurseForgeModsResponse = """
            {
              "data": [
                {
                  "id": 9001,
                  "logo": { "url": "https://cdn.example/curseforge.png" }
                }
              ]
            }
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/curseforge.png"] = iconBytes
            }
        };
        var service = CreateService(handler, new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        var iconSource = Assert.Single(result).Value;
        Assert.True(File.Exists(new Uri(iconSource).LocalPath));
        Assert.Equal(1, handler.CurseForgeFingerprintRequestCount);
        Assert.Equal(1, handler.CurseForgeModsRequestCount);
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncSkipsCurseForgeWhenApiKeyIsMissing()
    {
        var jarPath = await WriteModFileAsync("missing-key.jar", "missing-key-content");
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = "{}",
            ThrowOnCurseForgeRequest = true
        };
        var service = CreateService(handler, new StubCurseForgeApiKeyResolver(null));

        var result = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.Empty(result);
        Assert.Equal(0, handler.CurseForgeFingerprintRequestCount);
        Assert.Equal(0, handler.CurseForgeModsRequestCount);
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncDoesNotCacheInvalidIcon()
    {
        var jarPath = await WriteModFileAsync("invalid-icon.jar", "invalid-icon-content");
        var sha1 = await ComputeSha1Async(jarPath);
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = $$"""
            {
              "{{sha1}}": { "project_id": "invalid-project" }
            }
            """,
            ModrinthProjectsResponse = """
            [
              { "id": "invalid-project", "icon_url": "https://cdn.example/invalid.png" }
            ]
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/invalid.png"] = "not an image"u8.ToArray()
            }
        };
        var service = CreateService(handler);

        var result = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.Empty(result);
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Assert.False(Directory.Exists(cacheDirectory)
                     && Directory.EnumerateFiles(cacheDirectory, "*.png").Any());
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncKeepsValidIconsWhenAnotherRemoteIconIsInvalid()
    {
        var validJarPath = await WriteModFileAsync("valid-icon.jar", "valid-icon-content");
        var invalidJarPath = await WriteModFileAsync("invalid-batch-icon.jar", "invalid-batch-icon-content");
        var validSha1 = await ComputeSha1Async(validJarPath);
        var invalidSha1 = await ComputeSha1Async(invalidJarPath);
        var validIconBytes = CreatePngBytes(Colors.MediumSeaGreen);
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = $$"""
            {
              "{{validSha1}}": { "project_id": "valid-project" },
              "{{invalidSha1}}": { "project_id": "invalid-project" }
            }
            """,
            ModrinthProjectsResponse = """
            [
              { "id": "valid-project", "icon_url": "https://cdn.example/valid.png" },
              { "id": "invalid-project", "icon_url": "https://cdn.example/invalid.png" }
            ]
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/valid.png"] = validIconBytes,
                ["https://cdn.example/invalid.png"] = "not an image"u8.ToArray()
            }
        };
        var service = CreateService(handler);
        var progressReports = new List<IReadOnlyDictionary<string, string>>();

        var result = await service.ResolveMissingIconSourcesAsync(
            [CreateLocalMod(validJarPath), CreateLocalMod(invalidJarPath)],
            progress: new CapturingProgress(progressReports));

        var pair = Assert.Single(result);
        Assert.Equal(validJarPath, pair.Key);
        Assert.True(File.Exists(new Uri(pair.Value).LocalPath));
        var progressPair = Assert.Single(Assert.Single(progressReports));
        Assert.Equal(validJarPath, progressPair.Key);
        Assert.Equal(pair.Value, progressPair.Value);
        Assert.Equal(2, handler.IconDownloadRequestCount);
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncReturnsStaleCacheWhenRefreshIconIsInvalid()
    {
        var jarPath = await WriteModFileAsync("stale-refresh.jar", "stale-refresh-content");
        var sha1 = await ComputeSha1Async(jarPath);
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Directory.CreateDirectory(cacheDirectory);
        var cachedIconPath = Path.Combine(cacheDirectory, "stale.png");
        await File.WriteAllBytesAsync(cachedIconPath, CreatePngBytes(Colors.SteelBlue));
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-31);
        await File.WriteAllTextAsync(
            Path.Combine(cacheDirectory, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    entries = new Dictionary<string, object>
                    {
                        ["modrinth:stale-project"] = new
                        {
                            source = "modrinth",
                            projectId = "stale-project",
                            iconUrl = "https://cdn.example/old.png",
                            fileName = "stale.png",
                            cachedAt = oldTimestamp,
                            lastUsedAt = oldTimestamp,
                            sizeBytes = new FileInfo(cachedIconPath).Length
                        }
                    },
                    aliases = new Dictionary<string, string>
                    {
                        [$"sha1:{sha1}"] = "modrinth:stale-project"
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = $$"""
            {
              "{{sha1}}": { "project_id": "stale-project" }
            }
            """,
            ModrinthProjectsResponse = """
            [
              { "id": "stale-project", "icon_url": "https://cdn.example/invalid-refresh.png" }
            ]
            """,
            IconBytesByUrl =
            {
                ["https://cdn.example/invalid-refresh.png"] = "not an image"u8.ToArray()
            }
        };
        var service = CreateService(handler);
        var progressReports = new List<IReadOnlyDictionary<string, string>>();

        var result = await service.ResolveMissingIconSourcesAsync(
            [CreateLocalMod(jarPath)],
            progress: new CapturingProgress(progressReports));

        var iconSource = Assert.Single(result).Value;
        Assert.Equal(cachedIconPath, new Uri(iconSource).LocalPath);
        Assert.Equal(iconSource, Assert.Single(Assert.Single(progressReports)).Value);
        Assert.True(File.Exists(cachedIconPath));
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncCleansOldUnusedCacheEntries()
    {
        var jarPath = await WriteModFileAsync("cleanup.jar", "cleanup-content");
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Directory.CreateDirectory(cacheDirectory);
        var oldIconPath = Path.Combine(cacheDirectory, "old.png");
        await File.WriteAllBytesAsync(oldIconPath, CreatePngBytes(Colors.Red));
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-120);
        await File.WriteAllTextAsync(
            Path.Combine(cacheDirectory, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    entries = new Dictionary<string, object>
                    {
                        ["modrinth:old-project"] = new
                        {
                            source = "modrinth",
                            projectId = "old-project",
                            iconUrl = "https://cdn.example/old.png",
                            fileName = "old.png",
                            cachedAt = oldTimestamp,
                            lastUsedAt = oldTimestamp,
                            sizeBytes = new FileInfo(oldIconPath).Length
                        }
                    },
                    aliases = new Dictionary<string, string>
                    {
                        ["sha1:old"] = "modrinth:old-project"
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = "{}"
        };
        var service = CreateService(handler);

        _ = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.False(File.Exists(oldIconPath));
        var indexJson = await File.ReadAllTextAsync(Path.Combine(cacheDirectory, "index.json"));
        Assert.DoesNotContain("old-project", indexJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveMissingIconSourcesAsyncTrimsCacheWhenTotalSizeExceedsLimit()
    {
        var jarPath = await WriteModFileAsync("trim.jar", "trim-content");
        var cacheDirectory = Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName, "cache", "mods", "remote-icons");
        Directory.CreateDirectory(cacheDirectory);
        var oldIconPath = Path.Combine(cacheDirectory, "old-large.png");
        var recentIconPath = Path.Combine(cacheDirectory, "recent-large.png");
        CreateFileWithLength(oldIconPath, 25L * 1024L * 1024L);
        CreateFileWithLength(recentIconPath, 30L * 1024L * 1024L);
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(
            Path.Combine(cacheDirectory, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    entries = new Dictionary<string, object>
                    {
                        ["modrinth:old-large"] = new
                        {
                            source = "modrinth",
                            projectId = "old-large",
                            iconUrl = "https://cdn.example/old-large.png",
                            fileName = "old-large.png",
                            cachedAt = now,
                            lastUsedAt = now.AddDays(-10),
                            sizeBytes = new FileInfo(oldIconPath).Length
                        },
                        ["modrinth:recent-large"] = new
                        {
                            source = "modrinth",
                            projectId = "recent-large",
                            iconUrl = "https://cdn.example/recent-large.png",
                            fileName = "recent-large.png",
                            cachedAt = now,
                            lastUsedAt = now,
                            sizeBytes = new FileInfo(recentIconPath).Length
                        }
                    },
                    aliases = new Dictionary<string, string>
                    {
                        ["sha1:old-large"] = "modrinth:old-large",
                        ["sha1:recent-large"] = "modrinth:recent-large"
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        var handler = new ModIconLookupHandler
        {
            ModrinthVersionFilesResponse = "{}"
        };
        var service = CreateService(handler);

        _ = await service.ResolveMissingIconSourcesAsync([CreateLocalMod(jarPath)]);

        Assert.False(File.Exists(oldIconPath));
        Assert.True(File.Exists(recentIconPath));
        var indexJson = await File.ReadAllTextAsync(Path.Combine(cacheDirectory, "index.json"));
        Assert.DoesNotContain("old-large", indexJson, StringComparison.Ordinal);
        Assert.Contains("recent-large", indexJson, StringComparison.Ordinal);
    }

    private LocalModIconEnrichmentService CreateService(
        ModIconLookupHandler handler,
        ICurseForgeApiKeyResolver? curseForgeApiKeyResolver = null)
    {
        return new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            new HttpClient(handler),
            curseForgeApiKeyResolver ?? new StubCurseForgeApiKeyResolver(null));
    }

    private async Task<string> WriteModFileAsync(string fileName, string content)
    {
        var path = Path.Combine(TempRoot, "mods", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static LocalMod CreateLocalMod(string path)
    {
        return new LocalMod
        {
            Name = Path.GetFileNameWithoutExtension(path),
            FileName = Path.GetFileName(path),
            FullPath = path,
            IsEnabled = true,
            SizeBytes = new FileInfo(path).Length,
            Source = "Local"
        };
    }

    private static async Task<string> ComputeSha1Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static string CreateFileAlias(string path)
    {
        var fileInfo = new FileInfo(path);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"file:{Path.GetFullPath(fileInfo.FullName)}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
    }

    private static byte[] CreatePngBytes(Color color)
    {
        var pixels = new[] { color.B, color.G, color.R, color.A };
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void CreateFileWithLength(string path, long length)
    {
        using var stream = File.Create(path);
        stream.SetLength(length);
    }

    private sealed class StubCurseForgeApiKeyResolver(string? apiKey) : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(apiKey);
        }
    }

    private sealed class CapturingProgress(List<IReadOnlyDictionary<string, string>> reports)
        : IProgress<IReadOnlyDictionary<string, string>>
    {
        public void Report(IReadOnlyDictionary<string, string> value)
        {
            reports.Add(value);
        }
    }

    private sealed class ModIconLookupHandler : HttpMessageHandler
    {
        public string ModrinthVersionFilesResponse { get; init; } = "{}";

        public string ModrinthProjectsResponse { get; init; } = "[]";

        public string CurseForgeModsResponse { get; init; } = """{ "data": [] }""";

        public Dictionary<string, byte[]> IconBytesByUrl { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOnUnexpectedRequest { get; init; }

        public bool ThrowOnCurseForgeRequest { get; init; }

        public int ModrinthVersionFileRequestCount { get; private set; }

        public int ModrinthProjectRequestCount { get; private set; }

        public int CurseForgeFingerprintRequestCount { get; private set; }

        public int CurseForgeModsRequestCount { get; private set; }

        public int IconDownloadRequestCount { get; private set; }

        public int TotalRequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TotalRequestCount++;
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (ThrowOnUnexpectedRequest)
                throw new InvalidOperationException($"Unexpected request: {url}");

            if (request.Method == HttpMethod.Post && url.EndsWith("/version_files", StringComparison.Ordinal))
            {
                ModrinthVersionFileRequestCount++;
                return JsonResponse(ModrinthVersionFilesResponse);
            }

            if (request.Method == HttpMethod.Get && url.StartsWith("https://api.modrinth.com/v2/projects", StringComparison.Ordinal))
            {
                ModrinthProjectRequestCount++;
                return JsonResponse(ModrinthProjectsResponse);
            }

            if (request.Method == HttpMethod.Post && url.EndsWith("/fingerprints/432", StringComparison.Ordinal))
            {
                if (ThrowOnCurseForgeRequest)
                    throw new InvalidOperationException("CurseForge should not be requested.");

                CurseForgeFingerprintRequestCount++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(body);
                var fingerprint = document.RootElement
                    .GetProperty("fingerprints")
                    .EnumerateArray()
                    .First()
                    .GetInt64();
                return JsonResponse($$"""
                {
                  "data": {
                    "exactMatches": [
                      {
                        "id": 9001,
                        "file": { "fileFingerprint": {{fingerprint}} }
                      }
                    ]
                  }
                }
                """);
            }

            if (request.Method == HttpMethod.Post && url.EndsWith("/mods", StringComparison.Ordinal))
            {
                if (ThrowOnCurseForgeRequest)
                    throw new InvalidOperationException("CurseForge should not be requested.");

                CurseForgeModsRequestCount++;
                return JsonResponse(CurseForgeModsResponse);
            }

            if (request.Method == HttpMethod.Get && IconBytesByUrl.TryGetValue(url, out var bytes))
            {
                IconDownloadRequestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
