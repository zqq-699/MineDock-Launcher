using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Updates;

public sealed class RemoteManifestLauncherUpdateService : ILauncherUpdateService
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumSignatureFileBytes = 4 * 1024;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient httpClient;
    private readonly ILogger<RemoteManifestLauncherUpdateService>? logger;
    private readonly IReadOnlyList<LauncherUpdateManifestSource> manifestSources;
    private readonly IUpdateManifestSignatureVerifier? signatureVerifier;

    public RemoteManifestLauncherUpdateService(
        HttpClient? httpClient = null,
        ILogger<RemoteManifestLauncherUpdateService>? logger = null)
        : this(httpClient, logger, LauncherUpdateManifestSource.DefaultSources, signatureVerifier: null)
    {
    }

    internal RemoteManifestLauncherUpdateService(
        HttpClient? httpClient,
        ILogger<RemoteManifestLauncherUpdateService>? logger,
        IReadOnlyList<LauncherUpdateManifestSource> manifestSources)
        : this(httpClient, logger, manifestSources, signatureVerifier: null)
    {
    }

    internal RemoteManifestLauncherUpdateService(
        HttpClient? httpClient,
        ILogger<RemoteManifestLauncherUpdateService>? logger,
        IReadOnlyList<LauncherUpdateManifestSource> manifestSources,
        IUpdateManifestSignatureVerifier? signatureVerifier)
    {
        this.httpClient = httpClient ?? OfficialUpdateHttp.CreateClient(DefaultRequestTimeout);
        this.logger = logger;
        this.manifestSources = manifestSources.OrderBy(source => source.Priority).ToArray();
        this.signatureVerifier = signatureVerifier ?? TryCreateEmbeddedVerifier(logger);
        EnsureDefaultHeaders(this.httpClient);
    }

    public async Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        LauncherUpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (signatureVerifier is null)
            return LauncherUpdateCheckResult.Failed(currentVersion, "Signed launcher updates are unavailable in this build.");
        if (!TryCalculateVersionCode(currentVersion, out var currentVersionCode))
            return LauncherUpdateCheckResult.Failed(currentVersion, "The current launcher version is invalid.");

        var channelText = channel is LauncherUpdateChannel.Beta ? "beta" : "release";
        var sourceResults = await Task.WhenAll(manifestSources.Select(source =>
            LoadSourceAsync(source, channelText, cancellationToken))).ConfigureAwait(false);

        var invalid = sourceResults.FirstOrDefault(result => result.Status is ManifestSourceStatus.Invalid);
        if (invalid is not null)
        {
            logger?.LogError(
                "Launcher update manifest security validation failed. Source={Source} Reason={Reason}",
                invalid.Source.Name,
                invalid.FailureReason);
            return LauncherUpdateCheckResult.Failed(currentVersion, "Update manifest security validation failed.");
        }

        var valid = sourceResults.Where(result => result.Status is ManifestSourceStatus.Valid).ToArray();
        if (valid.Length == 0)
        {
            logger?.LogWarning("All launcher update manifest sources were unavailable. Channel={Channel}", channelText);
            return LauncherUpdateCheckResult.Failed(currentVersion, "All update manifest sources were unavailable.");
        }

        if (valid.Length > 1)
        {
            var expectedManifest = valid[0].ManifestBytes!;
            var expectedSignature = valid[0].SignatureFileBytes!;
            if (valid.Skip(1).Any(result =>
                    !CryptographicOperations.FixedTimeEquals(expectedManifest, result.ManifestBytes!)
                    || !CryptographicOperations.FixedTimeEquals(expectedSignature, result.SignatureFileBytes!)))
            {
                logger?.LogError("Valid launcher update mirrors returned different signed content. Channel={Channel}", channelText);
                return LauncherUpdateCheckResult.Failed(currentVersion, "Official update mirrors returned inconsistent signed manifests.");
            }
        }

        var selected = valid.OrderBy(result => result.Source.Priority).First();
        logger?.LogInformation(
            "Verified launcher update manifest. Source={Source} Channel={Channel} KeyId={KeyId} VersionCode={VersionCode}",
            selected.Source.Name,
            channelText,
            selected.Manifest!.KeyId,
            selected.Manifest.VersionCode);
        return CreateResult(currentVersion, currentVersionCode, selected.Manifest!);
    }

    private async Task<ManifestSourceResult> LoadSourceAsync(
        LauncherUpdateManifestSource source,
        string expectedChannel,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(source.CreateManifestUrl(expectedChannel), UriKind.Absolute, out var manifestUri))
                throw new UpdateSecurityException("The update manifest URL is invalid.");
            var signatureUri = new Uri(manifestUri.AbsoluteUri + ".sig", UriKind.Absolute);
            var manifestTask = DownloadBytesAsync(manifestUri, MaximumManifestBytes, cancellationToken);
            var signatureTask = DownloadBytesAsync(signatureUri, MaximumSignatureFileBytes, cancellationToken);
            try
            {
                await Task.WhenAll(manifestTask, signatureTask).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                var failures = new[] { manifestTask.Exception, signatureTask.Exception }
                    .Where(exception => exception is not null)
                    .SelectMany(exception => exception!.Flatten().InnerExceptions)
                    .ToArray();
                var securityFailure = failures.FirstOrDefault(exception => exception is UpdateSecurityException);
                if (securityFailure is not null)
                    throw new UpdateSecurityException("An update manifest response failed security validation.", securityFailure);
                var unavailableFailure = failures.FirstOrDefault(exception => exception is UpdateSourceUnavailableException);
                if (unavailableFailure is not null)
                    throw new UpdateSourceUnavailableException("An update manifest response was unavailable.", unavailableFailure);
                throw;
            }
            var manifestBytes = await manifestTask.ConfigureAwait(false);
            var signatureFileBytes = await signatureTask.ConfigureAwait(false);
            var signatureBytes = EmbeddedUpdateManifestSignatureVerifier.DecodeSignature(signatureFileBytes);
            if (!signatureVerifier!.Verify(manifestBytes, signatureBytes))
                throw new UpdateSecurityException("The update manifest signature is invalid.");

            var manifest = JsonSerializer.Deserialize<RemoteUpdateManifestDto>(manifestBytes, JsonOptions)
                ?? throw new UpdateSecurityException("The update manifest JSON is empty.");
            ValidateManifest(manifest, expectedChannel, signatureVerifier.KeyId);
            return ManifestSourceResult.Valid(source, manifest, manifestBytes, signatureFileBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UpdateSourceUnavailableException exception)
        {
            logger?.LogWarning(exception, "Launcher update manifest source unavailable. Source={Source}", source.Name);
            return ManifestSourceResult.Unavailable(source, exception.Message);
        }
        catch (Exception exception) when (exception is UpdateSecurityException or JsonException or InvalidDataException)
        {
            logger?.LogError(exception, "Launcher update manifest source failed security validation. Source={Source}", source.Name);
            return ManifestSourceResult.Invalid(source, exception.Message);
        }
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        try
        {
            using var response = await OfficialUpdateHttp.SendAsync(
                httpClient, uri, OfficialUpdateUriKind.Manifest, timeout.Token).ConfigureAwait(false);
            return await OfficialUpdateHttp.ReadLimitedBytesAsync(response, maximumBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateSourceUnavailableException("The update manifest source timed out.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new UpdateSourceUnavailableException("The update manifest source could not be read.", exception);
        }
    }

    private static LauncherUpdateCheckResult CreateResult(
        string currentVersion,
        int currentVersionCode,
        RemoteUpdateManifestDto manifest)
    {
        if (manifest.VersionCode <= currentVersionCode)
            return LauncherUpdateCheckResult.Latest(currentVersion);
        var asset = manifest.Assets.Single();
        var urls = asset.Urls.OrderBy(url => url.Priority)
            .Select(url => new LauncherUpdateDownloadUrl(url.Name.Trim(), url.Url.Trim(), url.Priority))
            .ToArray();
        var versionName = manifest.VersionName.Trim();
        return LauncherUpdateCheckResult.Available(currentVersion, new LauncherUpdateInfo(
            versionName,
            versionName,
            LauncherProjectLinks.GitHubReleasesUrl,
            urls[0].Url,
            string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? null : manifest.ReleaseNotes,
            asset.FileName.Trim(),
            LauncherUpdateAssetKind.WindowsX64Executable,
            asset.Size,
            asset.Sha256.Trim().ToLowerInvariant(),
            manifest.KeyId.Trim().ToLowerInvariant(),
            VersionCode: manifest.VersionCode,
            IsMandatory: manifest.Mandatory || currentVersionCode < manifest.MinSupportedVersionCode,
            MinSupportedVersionCode: manifest.MinSupportedVersionCode,
            PublishedAt: manifest.PublishedAt,
            DownloadUrls: urls));
    }

    private static void ValidateManifest(RemoteUpdateManifestDto manifest, string expectedChannel, string trustedKeyId)
    {
        if (manifest.SchemaVersion != 1
            || !string.Equals(manifest.AppId?.Trim(), "BlockHelm-Launcher", StringComparison.Ordinal)
            || !string.Equals(manifest.Channel?.Trim(), expectedChannel, StringComparison.OrdinalIgnoreCase)
            || manifest.VersionCode < 0
            || string.IsNullOrWhiteSpace(manifest.VersionName)
            || !IsHex64(manifest.KeyId)
            || !string.Equals(manifest.KeyId.Trim(), trustedKeyId, StringComparison.Ordinal)
            || manifest.Assets.Count != 1)
        {
            throw new UpdateSecurityException("The signed update manifest metadata is invalid.");
        }

        var asset = manifest.Assets[0];
        if (!string.Equals(asset.Platform?.Trim(), "windows", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(asset.Arch?.Trim(), "x64", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(asset.PackageType?.Trim(), "exe", StringComparison.OrdinalIgnoreCase)
            || asset.Size <= 0
            || !IsHex64(asset.Sha256)
            || string.IsNullOrWhiteSpace(asset.FileName)
            || !string.Equals(Path.GetFileName(asset.FileName), asset.FileName, StringComparison.Ordinal)
            || !asset.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || asset.Urls.Count == 0)
        {
            throw new UpdateSecurityException("The signed update executable metadata is invalid.");
        }

        foreach (var url in asset.Urls)
        {
            if (string.IsNullOrWhiteSpace(url.Name)
                || !Uri.TryCreate(url.Url, UriKind.Absolute, out var uri))
                throw new UpdateSecurityException("The signed update download URL is invalid.");
            OfficialUpdateHttp.ValidateInitialUri(uri, OfficialUpdateUriKind.Executable);
        }
    }

    private static bool IsHex64(string? value) => value?.Trim() is { Length: 64 } text && text.All(Uri.IsHexDigit);

    private static IUpdateManifestSignatureVerifier? TryCreateEmbeddedVerifier(
        ILogger<RemoteManifestLauncherUpdateService>? logger)
    {
        try
        {
            return new EmbeddedUpdateManifestSignatureVerifier();
        }
        catch (UpdateSecurityException exception)
        {
            logger?.LogWarning(exception, "The update signing public key is unavailable; remote updates are disabled.");
            return null;
        }
    }

    private static void EnsureDefaultHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherProjectLinks.GitHubUserAgent);
        if (!client.DefaultRequestHeaders.Accept.Any(header => header.MediaType == "application/json"))
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static bool TryCalculateVersionCode(string? value, out int versionCode)
    {
        versionCode = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0) text = text[..metadataIndex];
        var betaRevision = 99;
        var preReleaseIndex = text.IndexOf('-');
        if (preReleaseIndex >= 0)
        {
            var preRelease = text[(preReleaseIndex + 1)..];
            text = text[..preReleaseIndex];
            if (!preRelease.StartsWith("beta.", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(preRelease[5..], out betaRevision)
                || betaRevision is <= 0 or > 98) return false;
        }
        var segments = text.Split('.');
        var patch = 0;
        if (segments.Length is < 2 or > 3
            || !int.TryParse(segments[0], out var major)
            || !int.TryParse(segments[1], out var minor)
            || (segments.Length == 3 && !int.TryParse(segments[2], out patch))) return false;
        if (major is < 0 or > 99 || minor is < 0 or > 99 || patch is < 0 or > 99) return false;
        versionCode = major * 1_000_000 + minor * 10_000 + patch * 100 + betaRevision;
        return true;
    }

    internal sealed class RemoteUpdateManifestDto
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
        [JsonPropertyName("appId")] public string? AppId { get; init; }
        [JsonPropertyName("keyId")] public string KeyId { get; init; } = string.Empty;
        [JsonPropertyName("channel")] public string? Channel { get; init; }
        [JsonPropertyName("versionName")] public string VersionName { get; init; } = string.Empty;
        [JsonPropertyName("versionCode")] public int VersionCode { get; init; }
        [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }
        [JsonPropertyName("mandatory")] public bool Mandatory { get; init; }
        [JsonPropertyName("minSupportedVersionCode")] public int MinSupportedVersionCode { get; init; }
        [JsonPropertyName("releaseNotes")] public string? ReleaseNotes { get; init; }
        [JsonPropertyName("assets")] public List<RemoteUpdateAssetDto> Assets { get; init; } = [];
    }

    internal sealed class RemoteUpdateAssetDto
    {
        [JsonPropertyName("platform")] public string? Platform { get; init; }
        [JsonPropertyName("arch")] public string? Arch { get; init; }
        [JsonPropertyName("packageType")] public string? PackageType { get; init; }
        [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("sha256")] public string Sha256 { get; init; } = string.Empty;
        [JsonPropertyName("urls")] public List<RemoteUpdateUrlDto> Urls { get; init; } = [];
    }

    internal sealed class RemoteUpdateUrlDto
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
        [JsonPropertyName("priority")] public int Priority { get; init; }
    }

    private enum ManifestSourceStatus { Valid, Unavailable, Invalid }
    private sealed record ManifestSourceResult(
        LauncherUpdateManifestSource Source,
        ManifestSourceStatus Status,
        RemoteUpdateManifestDto? Manifest,
        byte[]? ManifestBytes,
        byte[]? SignatureFileBytes,
        string? FailureReason)
    {
        public static ManifestSourceResult Valid(LauncherUpdateManifestSource source, RemoteUpdateManifestDto manifest, byte[] bytes, byte[] signature) =>
            new(source, ManifestSourceStatus.Valid, manifest, bytes, signature, null);
        public static ManifestSourceResult Unavailable(LauncherUpdateManifestSource source, string reason) =>
            new(source, ManifestSourceStatus.Unavailable, null, null, null, reason);
        public static ManifestSourceResult Invalid(LauncherUpdateManifestSource source, string reason) =>
            new(source, ManifestSourceStatus.Invalid, null, null, null, reason);
    }
}

public sealed record LauncherUpdateManifestSource(string Name, string UrlTemplate, int Priority)
{
    public static IReadOnlyList<LauncherUpdateManifestSource> DefaultSources { get; } =
    [
        new("gitee", LauncherProjectLinks.GiteeUpdateManifestUrlTemplate, 1),
        new("github", LauncherProjectLinks.GitHubUpdateManifestUrlTemplate, 2)
    ];

    public string CreateManifestUrl(string channel) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture, UrlTemplate, channel);
}
