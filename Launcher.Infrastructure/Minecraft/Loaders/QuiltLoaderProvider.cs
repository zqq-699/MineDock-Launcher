using System.Net;
using System.Net.Http;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed class QuiltLoaderProvider : ILoaderProvider
{
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;

    public QuiltLoaderProvider(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<QuiltLoaderProvider>? logger = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger<QuiltLoaderProvider>.Instance;
    }

    public LoaderKind Kind => LoaderKind.Quilt;

    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        logger.LogInformation(
            "Loading Quilt versions. MinecraftVersion={MinecraftVersion}",
            minecraftVersion);

        try
        {
            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                logger,
                DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
                category: DownloadConcurrencyCategory.Metadata);
            using var response = await executor.GetAsync(
                $"https://meta.quiltmc.org/v3/versions/loader/{minecraftVersion}",
                downloadSourcePreference,
                categoryHint: "Quilt",
                cancellationToken);

            if (response.Response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                return [];

            response.Response.EnsureSuccessStatusCode();

            await using var stream = await response.Response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind is not JsonValueKind.Array)
                return [];

            var versions = json.RootElement
                .EnumerateArray()
                .Select(ReadLoaderVersion)
                .Where(version => version is not null)
                .Select(version => version!)
                .GroupBy(version => version.Version, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(version => version.IsStable)
                .ThenByDescending(version => ParseVersionKey(version.Version), QuiltVersionKeyComparer.Instance)
                .ThenByDescending(version => version.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation(
                "Loaded Quilt versions. MinecraftVersion={MinecraftVersion} Count={Count}",
                minecraftVersion,
                versions.Count);
            return versions;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    public async Task<string> InstallAsync(
        string minecraftVersion,
        string gameDirectory,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        var selectedLoaderVersion = loaderVersion;

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableLoaders = await GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            selectedLoaderVersion = availableLoaders.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Quilt loader version available for {minecraftVersion}.");

        logger.LogInformation(
            "Installing Quilt. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} TargetVersionName={TargetVersionName}",
            minecraftVersion,
            selectedLoaderVersion,
            isolatedVersionName);

        try
        {
            var finalVersionName = await QuiltVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                selectedLoaderVersion,
                isolatedVersionName,
                gameDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken);

            var launcher = VanillaLoaderProvider.CreateLauncher(
                gameDirectory,
                progress,
                downloadSourcePreference,
                logger,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState);
            VanillaLoaderProvider.AttachProgress(launcher, progress);
            await launcher.InstallAsync(finalVersionName, cancellationToken);

            logger.LogInformation(
                "Quilt installation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} FinalVersionName={FinalVersionName}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName);
            return finalVersionName;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Quilt installation failed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} TargetVersionName={TargetVersionName}",
                minecraftVersion,
                selectedLoaderVersion,
                isolatedVersionName);
            throw;
        }
    }

    private static LoaderVersionInfo? ReadLoaderVersion(JsonElement item)
    {
        if (!item.TryGetProperty("loader", out var loader)
            || loader.ValueKind is not JsonValueKind.Object
            || !loader.TryGetProperty("version", out var versionProperty)
            || versionProperty.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var version = versionProperty.GetString();
        if (string.IsNullOrWhiteSpace(version))
            return null;

        return new LoaderVersionInfo(version, IsStableVersion(version));
    }

    private static bool IsStableVersion(string version)
    {
        return version.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("-alpha", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("-rc", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("+snapshot", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("+pre", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static QuiltVersionKey ParseVersionKey(string version)
    {
        var numericPart = version;
        var suffixIndex = version.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            numericPart = version[..suffixIndex];

        var values = numericPart
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, out var value) ? value : 0)
            .ToArray();
        return new QuiltVersionKey(values);
    }

    private readonly record struct QuiltVersionKey(int[] Parts);

    private sealed class QuiltVersionKeyComparer : IComparer<QuiltVersionKey>
    {
        public static QuiltVersionKeyComparer Instance { get; } = new();

        public int Compare(QuiltVersionKey left, QuiltVersionKey right)
        {
            var length = Math.Max(left.Parts.Length, right.Parts.Length);
            for (var index = 0; index < length; index++)
            {
                var leftValue = index < left.Parts.Length ? left.Parts[index] : 0;
                var rightValue = index < right.Parts.Length ? right.Parts[index] : 0;
                var comparison = leftValue.CompareTo(rightValue);
                if (comparison != 0)
                    return comparison;
            }

            return 0;
        }
    }
}
