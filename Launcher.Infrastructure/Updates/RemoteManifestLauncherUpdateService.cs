/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

/// <summary>
/// 下载并校验远端更新清单，按渠道和平台选择可用资产与镜像地址。
/// </summary>
public sealed class RemoteManifestLauncherUpdateService : ILauncherUpdateService
{
    // 清单是安全边界：版本、渠道、哈希和 URL 均需验证后才能交给自更新服务。
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly ILogger<RemoteManifestLauncherUpdateService>? logger;
    private readonly IReadOnlyList<LauncherUpdateManifestSource> manifestSources;

    public RemoteManifestLauncherUpdateService(
        HttpClient? httpClient = null,
        ILogger<RemoteManifestLauncherUpdateService>? logger = null)
        : this(httpClient, logger, LauncherUpdateManifestSource.DefaultSources)
    {
    }

    public RemoteManifestLauncherUpdateService(
        HttpClient? httpClient,
        ILogger<RemoteManifestLauncherUpdateService>? logger,
        IReadOnlyList<LauncherUpdateManifestSource> manifestSources)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = DefaultRequestTimeout
        };
        this.logger = logger;
        this.manifestSources = manifestSources
            .OrderBy(source => source.Priority)
            .ToArray();
        EnsureDefaultHeaders(this.httpClient);
    }

    public async Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        LauncherUpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        // 每个清单源按配置顺序回退，单个镜像失败不应阻止检查其他官方候选。
        if (!TryCalculateVersionCode(currentVersion, out var currentVersionCode))
        {
            logger?.LogWarning("Unable to parse current launcher version for update check: {CurrentVersion}", currentVersion);
            return LauncherUpdateCheckResult.Failed(currentVersion);
        }

        var channelText = ToManifestChannelText(channel);
        var failures = new List<string>();
        foreach (var source in manifestSources)
        {
            var manifestUrl = source.CreateManifestUrl(channelText);
            try
            {
                logger?.LogInformation(
                    "Checking launcher updates from remote manifest. Source={Source} Channel={Channel} Url={Url}",
                    source.Name,
                    channelText,
                    manifestUrl);

                var manifest = await LoadManifestAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
                ValidateManifest(manifest, channelText);

                logger?.LogInformation(
                    "Launcher update manifest loaded. Source={Source} Channel={Channel} VersionName={VersionName} VersionCode={VersionCode}",
                    source.Name,
                    manifest.Channel,
                    manifest.VersionName,
                    manifest.VersionCode);

                return CreateResult(currentVersion, currentVersionCode, manifest);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{source.Name}: timeout");
                logger?.LogWarning(
                    "Launcher update manifest source timed out. Source={Source} Url={Url}",
                    source.Name,
                    manifestUrl);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{source.Name}: {ex.Message}");
                logger?.LogWarning(
                    ex,
                    "Launcher update manifest source failed. Source={Source} Url={Url}",
                    source.Name,
                    manifestUrl);
            }
        }

        logger?.LogWarning(
            "All launcher update manifest sources failed. Channel={Channel} Failures={Failures}",
            channelText,
            string.Join("; ", failures));
        return LauncherUpdateCheckResult.Failed(currentVersion, string.Join("; ", failures));
    }

    private async Task<RemoteUpdateManifestDto> LoadManifestAsync(
        string manifestUrl,
        CancellationToken cancellationToken)
    {
        // 独立超时与调用方取消链接，既限制挂起请求又保留用户取消语义。
        using var response = await httpClient.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Update manifest request failed with HTTP {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<RemoteUpdateManifestDto>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException("Update manifest JSON is empty.");
    }

    private static LauncherUpdateCheckResult CreateResult(
        string currentVersion,
        int currentVersionCode,
        RemoteUpdateManifestDto manifest)
    {
        // 只有远端 versionCode 严格更大才是更新，同版本不同构建不会触发降级或重复更新。
        if (manifest.VersionCode <= currentVersionCode)
            return LauncherUpdateCheckResult.Latest(currentVersion);

        var asset = SelectWindowsX64ExecutableAsset(manifest.Assets);
        var downloadUrls = SelectDownloadUrls(asset?.Urls);
        var downloadUrl = downloadUrls.Count > 0
            ? downloadUrls[0].Url
            : null;
        var update = new LauncherUpdateInfo(
            string.IsNullOrWhiteSpace(manifest.VersionName)
                ? manifest.VersionCode.ToString()
                : manifest.VersionName.Trim(),
            string.IsNullOrWhiteSpace(manifest.VersionName)
                ? manifest.VersionCode.ToString()
                : manifest.VersionName.Trim(),
            LauncherProjectLinks.GitHubReleasesUrl,
            downloadUrl,
            string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? null : manifest.ReleaseNotes,
            string.IsNullOrWhiteSpace(asset?.FileName) ? null : asset.FileName.Trim(),
            asset is null ? LauncherUpdateAssetKind.ReleasePage : LauncherUpdateAssetKind.WindowsX64Executable,
            manifest.VersionCode,
            manifest.Mandatory || currentVersionCode < manifest.MinSupportedVersionCode,
            manifest.MinSupportedVersionCode,
            manifest.PublishedAt,
            asset?.Size ?? 0,
            string.IsNullOrWhiteSpace(asset?.Sha256) ? null : asset.Sha256.Trim(),
            downloadUrls);

        return LauncherUpdateCheckResult.Available(currentVersion, update);
    }

    private static RemoteUpdateAssetDto? SelectWindowsX64ExecutableAsset(IReadOnlyList<RemoteUpdateAssetDto>? assets)
    {
        // 自更新当前只支持 Windows x64 单文件资产，不能误选压缩包或其他平台二进制。
        return assets?
            .FirstOrDefault(asset =>
                string.Equals(asset.Platform?.Trim(), "windows", StringComparison.OrdinalIgnoreCase)
                && string.Equals(asset.Arch?.Trim(), "x64", StringComparison.OrdinalIgnoreCase)
                && string.Equals(asset.PackageType?.Trim(), "exe", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<LauncherUpdateDownloadUrl> SelectDownloadUrls(IReadOnlyList<RemoteUpdateUrlDto>? urls)
    {
        if (urls is null || urls.Count == 0)
            return [];

        return urls
            .Where(url => Uri.TryCreate(url.Url, UriKind.Absolute, out var uri)
                          && uri.Scheme is "http" or "https")
            .OrderBy(url => url.Priority)
            .Select(url => new LauncherUpdateDownloadUrl(
                string.IsNullOrWhiteSpace(url.Name) ? "source" : url.Name.Trim(),
                url.Url!.Trim(),
                url.Priority))
            .ToArray();
    }

    private static void ValidateManifest(RemoteUpdateManifestDto manifest, string expectedChannel)
    {
        // 渠道不匹配视为无效清单，防止 stable 客户端意外接收 preview 资产。
        if (manifest.SchemaVersion != 1)
            throw new InvalidOperationException("Unsupported update manifest schema version.");

        if (!string.Equals(manifest.AppId?.Trim(), "BlockHelm-Launcher", StringComparison.Ordinal))
            throw new InvalidOperationException("Update manifest appId does not match this launcher.");

        if (!string.Equals(manifest.Channel?.Trim(), expectedChannel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update manifest channel does not match requested channel.");

        if (manifest.VersionCode < 0)
            throw new InvalidOperationException("Update manifest versionCode is invalid.");
    }

    private static void EnsureDefaultHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherProjectLinks.GitHubUserAgent);

        if (!client.DefaultRequestHeaders.Accept.Any(header =>
                string.Equals(header.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private static string ToManifestChannelText(LauncherUpdateChannel channel)
    {
        return channel is LauncherUpdateChannel.Beta ? "beta" : "release";
    }

    public static bool TryCalculateVersionCode(string? value, out int versionCode)
    {
        // 兼容 v 前缀和预发布后缀，但只用数值段生成可稳定比较的版本码。
        versionCode = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            text = text[1..];

        var metadataIndex = text.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
            text = text[..metadataIndex];

        var betaRevision = 99;
        var preReleaseIndex = text.IndexOf('-', StringComparison.Ordinal);
        if (preReleaseIndex >= 0)
        {
            var preRelease = text[(preReleaseIndex + 1)..];
            text = text[..preReleaseIndex];
            if (!preRelease.StartsWith("beta.", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(preRelease["beta.".Length..], out betaRevision)
                || betaRevision <= 0
                || betaRevision > 98)
            {
                return false;
            }
        }

        var segments = text.Split('.');
        if (segments.Length < 2)
            return false;

        if (!int.TryParse(segments[0], out var major)
            || !int.TryParse(segments[1], out var minor))
        {
            return false;
        }

        var patch = 0;
        if (segments.Length >= 3 && !int.TryParse(segments[2], out patch))
            return false;

        if (major < 0 || minor < 0 || patch < 0)
            return false;

        if (major > 99 || minor > 99 || patch > 99)
            return false;

        versionCode = major * 1000000 + minor * 10000 + patch * 100 + betaRevision;
        return true;
    }

    private sealed class RemoteUpdateManifestDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("appId")]
        public string? AppId { get; init; }

        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("versionName")]
        public string? VersionName { get; init; }

        [JsonPropertyName("versionCode")]
        public int VersionCode { get; init; }

        [JsonPropertyName("publishedAt")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; init; }

        [JsonPropertyName("minSupportedVersionCode")]
        public int MinSupportedVersionCode { get; init; }

        [JsonPropertyName("releaseNotes")]
        public string? ReleaseNotes { get; init; }

        [JsonPropertyName("assets")]
        public List<RemoteUpdateAssetDto> Assets { get; init; } = [];
    }

    private sealed class RemoteUpdateAssetDto
    {
        [JsonPropertyName("platform")]
        public string? Platform { get; init; }

        [JsonPropertyName("arch")]
        public string? Arch { get; init; }

        [JsonPropertyName("packageType")]
        public string? PackageType { get; init; }

        [JsonPropertyName("fileName")]
        public string? FileName { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("urls")]
        public List<RemoteUpdateUrlDto> Urls { get; init; } = [];
    }

    private sealed class RemoteUpdateUrlDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("priority")]
        public int Priority { get; init; }
    }
}

public sealed record LauncherUpdateManifestSource(
    string Name,
    string UrlTemplate,
    int Priority)
{
    public static IReadOnlyList<LauncherUpdateManifestSource> DefaultSources { get; } =
    [
        new LauncherUpdateManifestSource(
            "gitee",
            LauncherProjectLinks.GiteeUpdateManifestUrlTemplate,
            1),
        new LauncherUpdateManifestSource(
            "github",
            LauncherProjectLinks.GitHubUpdateManifestUrlTemplate,
            2)
    ];

    public string CreateManifestUrl(string channel)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            UrlTemplate,
            channel);
    }
}
