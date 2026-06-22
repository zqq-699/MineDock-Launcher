using System.Net;
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class MinecraftDownloadRequestExecutor
{
    private readonly HttpClient httpClient;
    private readonly ILogger logger;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;
    private readonly IImportConcurrencyLimiter limiter;
    private readonly DownloadConcurrencyCategory category;

    public MinecraftDownloadRequestExecutor(
        HttpClient httpClient,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null,
        IImportConcurrencyLimiter? limiter = null,
        DownloadConcurrencyCategory category = DownloadConcurrencyCategory.Metadata)
    {
        this.httpClient = httpClient;
        this.logger = logger ?? NullLogger.Instance;
        this.bandwidthLimiter = bandwidthLimiter;
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
        this.category = category;
    }

    public async Task<ResolvedHttpResponse> GetAsync(
        string originalUrl,
        DownloadSourcePreference preference,
        string? categoryHint,
        CancellationToken cancellationToken)
    {
        DownloadSourceRequestException? lastException = null;
        var candidates = MinecraftDownloadSourceResolver.EnumerateRequests(originalUrl, preference, categoryHint).ToList();
        for (var index = 0; index < candidates.Count; index++)
        {
            var resolution = candidates[index];
            var hasFallback = index < candidates.Count - 1;
            var lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await httpClient.GetAsync(resolution.ActualUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode || !hasFallback)
                {
                    await DownloadResponseThrottler.ApplyAsync(response, bandwidthLimiter, cancellationToken, lease).ConfigureAwait(false);
                    LogResolvedRequest(resolution, response.StatusCode, fallbackUsed: index > 0);
                    return new ResolvedHttpResponse(resolution, response);
                }

                logger.LogWarning(
                    "Download request failed and will fall back to BMCLAPI. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} StatusCode={StatusCode}",
                    preference,
                    resolution.ResourceCategory,
                    resolution.OriginalUrl,
                    resolution.ActualUrl,
                    resolution.ResolvedSourceKind,
                    (int)response.StatusCode);
                response.Dispose();
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                var wrapped = new DownloadSourceRequestException(resolution, exception);
                if (!hasFallback)
                    throw wrapped;

                lastException = wrapped;
                logger.LogWarning(
                    exception,
                    "Download request threw before fallback. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind}",
                    preference,
                    resolution.ResourceCategory,
                    resolution.OriginalUrl,
                    resolution.ActualUrl,
                    resolution.ResolvedSourceKind);
            }
        }

        if (lastException is not null)
            throw lastException;

        throw new InvalidOperationException($"No download candidates were available for {originalUrl}.");
    }

    private ValueTask<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        return category switch
        {
            DownloadConcurrencyCategory.Metadata => limiter.AcquireMetadataSlotAsync(cancellationToken),
            DownloadConcurrencyCategory.Modpack => limiter.AcquireModpackDownloadSlotAsync(cancellationToken),
            DownloadConcurrencyCategory.Runtime => limiter.AcquireRuntimeDownloadSlotAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported download concurrency category.")
        };
    }

    private void LogResolvedRequest(ResolvedDownloadRequest resolution, HttpStatusCode statusCode, bool fallbackUsed)
    {
        logger.LogInformation(
            "Download request resolved. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} StatusCode={StatusCode} FallbackUsed={FallbackUsed}",
            resolution.RequestedSourcePreference,
            resolution.ResourceCategory,
            resolution.OriginalUrl,
            resolution.ActualUrl,
            resolution.ResolvedSourceKind,
            (int)statusCode,
            fallbackUsed);
    }

    internal sealed record ResolvedHttpResponse(
        ResolvedDownloadRequest Resolution,
        HttpResponseMessage Response)
        : IDisposable
    {
        public void Dispose()
        {
            Response.Dispose();
        }
    }

    internal sealed class DownloadSourceRequestException : Exception
    {
        public DownloadSourceRequestException(ResolvedDownloadRequest resolution, Exception innerException)
            : base($"Download request failed for {resolution.ActualUrl}", innerException)
        {
            Resolution = resolution;
        }

        public ResolvedDownloadRequest Resolution { get; }
    }
}
