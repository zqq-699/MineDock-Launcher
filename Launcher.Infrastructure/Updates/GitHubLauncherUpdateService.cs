using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Launcher.Application;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

public sealed class GitHubLauncherUpdateService : ILauncherUpdateService
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private const string GitHubApiVersion = "2022-11-28";

    private readonly HttpClient httpClient;
    private readonly ILogger<GitHubLauncherUpdateService>? logger;

    public GitHubLauncherUpdateService(
        HttpClient? httpClient = null,
        ILogger<GitHubLauncherUpdateService>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = DefaultRequestTimeout
        };
        this.logger = logger;
        EnsureDefaultHeaders(this.httpClient);
    }

    public async Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (!LauncherSemanticVersion.TryParse(currentVersion, out var parsedCurrentVersion))
        {
            logger?.LogWarning("Unable to parse current launcher version for update check: {CurrentVersion}", currentVersion);
            return LauncherUpdateCheckResult.Failed(currentVersion);
        }

        try
        {
            logger?.LogInformation("Checking launcher updates from GitHub Releases.");
            var releases = await httpClient.GetFromJsonAsync<List<GitHubReleaseDto>>(
                    LauncherProjectLinks.GitHubReleasesApiUrl,
                    cancellationToken)
                .ConfigureAwait(false);
            if (releases is null || releases.Count == 0)
            {
                logger?.LogInformation("No GitHub releases returned for launcher update check.");
                return LauncherUpdateCheckResult.Latest(currentVersion);
            }

            var latestRelease = releases
                .Where(release => !release.Draft)
                .Select(release => CreateReleaseCandidate(release))
                .Where(candidate => candidate is not null && candidate.Version.CompareTo(parsedCurrentVersion) > 0)
                .OrderByDescending(candidate => candidate!.Version)
                .FirstOrDefault();

            if (latestRelease is null)
            {
                logger?.LogInformation("Launcher is already up to date. Current version: {CurrentVersion}", currentVersion);
                return LauncherUpdateCheckResult.Latest(currentVersion);
            }

            var update = new LauncherUpdateInfo(
                latestRelease.Version.NormalizedText,
                latestRelease.Version.NormalizedText,
                latestRelease.ReleasePageUrl,
                latestRelease.DownloadUrl,
                latestRelease.Changelog,
                latestRelease.DownloadFileName,
                latestRelease.AssetKind);

            logger?.LogInformation(
                "Launcher update found. Current version: {CurrentVersion}, Remote version: {RemoteVersion}",
                currentVersion,
                update.Version);
            return LauncherUpdateCheckResult.Available(currentVersion, update);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Launcher update check failed.");
            return LauncherUpdateCheckResult.Failed(currentVersion, ex.Message);
        }
    }

    private static void EnsureDefaultHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherProjectLinks.GitHubUserAgent);

        if (!client.DefaultRequestHeaders.Accept.Any(header =>
                string.Equals(header.MediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase)))
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        if (!client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubApiVersion);
    }

    private static GitHubReleaseCandidate? CreateReleaseCandidate(GitHubReleaseDto release)
    {
        if (!LauncherSemanticVersion.TryParse(release.TagName, out var version))
            return null;

        var releasePageUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? LauncherProjectLinks.GitHubReleasesUrl
            : release.HtmlUrl.Trim();
        var selectedAsset = SelectDownloadAsset(release.Assets);

        return new GitHubReleaseCandidate(
            version,
            releasePageUrl,
            selectedAsset?.DownloadUrl ?? releasePageUrl,
            string.IsNullOrWhiteSpace(release.Body) ? null : release.Body,
            selectedAsset?.FileName,
            selectedAsset?.AssetKind ?? LauncherUpdateAssetKind.ReleasePage);
    }

    private static GitHubSelectedAsset? SelectDownloadAsset(IReadOnlyList<GitHubReleaseAssetDto>? assets)
    {
        if (assets is null || assets.Count == 0)
            return null;

        var executableAssets = assets
            .Where(IsDownloadableExecutableAsset)
            .ToList();
        var x64Executable = executableAssets.FirstOrDefault(asset =>
            asset.Name!.Contains("x64", StringComparison.OrdinalIgnoreCase)
            || asset.Name.Contains("win64", StringComparison.OrdinalIgnoreCase)
            || asset.Name.Contains("amd64", StringComparison.OrdinalIgnoreCase));
        if (x64Executable is not null)
        {
            return new GitHubSelectedAsset(
                x64Executable.BrowserDownloadUrl!.Trim(),
                x64Executable.Name!.Trim(),
                LauncherUpdateAssetKind.WindowsX64Executable);
        }

        var executable = executableAssets.FirstOrDefault();
        if (executable is not null)
        {
            return new GitHubSelectedAsset(
                executable.BrowserDownloadUrl!.Trim(),
                executable.Name!.Trim(),
                LauncherUpdateAssetKind.OtherExecutable);
        }

        return null;
    }

    private static bool IsDownloadableExecutableAsset(GitHubReleaseAssetDto asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            return false;

        var name = asset.Name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".exe.asc", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GitHubReleaseCandidate(
        LauncherSemanticVersion Version,
        string ReleasePageUrl,
        string DownloadUrl,
        string? Changelog,
        string? DownloadFileName,
        LauncherUpdateAssetKind AssetKind);

    private sealed record GitHubSelectedAsset(
        string DownloadUrl,
        string FileName,
        LauncherUpdateAssetKind AssetKind);

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDto> Assets { get; init; } = [];
    }

    private sealed class GitHubReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }

    private sealed record LauncherSemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? PreRelease) : IComparable<LauncherSemanticVersion>
    {
        public string NormalizedText => PreRelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

        public static bool TryParse(string? value, out LauncherSemanticVersion version)
        {
            version = default!;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                text = text[1..];

            var metadataIndex = text.IndexOf('+', StringComparison.Ordinal);
            if (metadataIndex >= 0)
                text = text[..metadataIndex];

            string? preRelease = null;
            var preReleaseIndex = text.IndexOf('-', StringComparison.Ordinal);
            if (preReleaseIndex >= 0)
            {
                preRelease = text[(preReleaseIndex + 1)..];
                text = text[..preReleaseIndex];
            }

            var segments = text.Split('.');
            if (segments.Length < 2 || segments.Length > 4)
                return false;

            if (!int.TryParse(segments[0], out var major)
                || !int.TryParse(segments[1], out var minor))
            {
                return false;
            }

            var patch = 0;
            if (segments.Length >= 3 && !int.TryParse(segments[2], out patch))
                return false;

            version = new LauncherSemanticVersion(
                major,
                minor,
                patch,
                string.IsNullOrWhiteSpace(preRelease) ? null : preRelease);
            return true;
        }

        public int CompareTo(LauncherSemanticVersion? other)
        {
            if (other is null)
                return 1;

            var coreComparison = Major.CompareTo(other.Major);
            if (coreComparison != 0)
                return coreComparison;

            coreComparison = Minor.CompareTo(other.Minor);
            if (coreComparison != 0)
                return coreComparison;

            coreComparison = Patch.CompareTo(other.Patch);
            if (coreComparison != 0)
                return coreComparison;

            if (PreRelease is null && other.PreRelease is null)
                return 0;
            if (PreRelease is null)
                return 1;
            if (other.PreRelease is null)
                return -1;

            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            var length = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < length; i++)
            {
                if (i >= leftParts.Length)
                    return -1;
                if (i >= rightParts.Length)
                    return 1;

                var leftIsNumber = int.TryParse(leftParts[i], out var leftNumber);
                var rightIsNumber = int.TryParse(rightParts[i], out var rightNumber);
                if (leftIsNumber && rightIsNumber)
                {
                    var numberComparison = leftNumber.CompareTo(rightNumber);
                    if (numberComparison != 0)
                        return numberComparison;

                    continue;
                }

                if (leftIsNumber)
                    return -1;
                if (rightIsNumber)
                    return 1;

                var textComparison = string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                if (textComparison != 0)
                    return textComparison;
            }

            return 0;
        }
    }
}
