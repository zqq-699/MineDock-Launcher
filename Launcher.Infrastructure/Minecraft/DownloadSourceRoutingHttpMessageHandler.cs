using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadSourceRoutingHttpMessageHandler : DelegatingHandler
{
    private readonly DownloadSourcePreference preference;
    private readonly DownloadConcurrencyCategory category;
    private readonly ILogger logger;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;
    private readonly IImportConcurrencyLimiter limiter;

    public DownloadSourceRoutingHttpMessageHandler(
        DownloadSourcePreference preference,
        DownloadConcurrencyCategory category,
        HttpMessageHandler innerHandler,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null,
        IImportConcurrencyLimiter? limiter = null)
        : base(innerHandler)
    {
        this.preference = preference;
        this.category = category;
        this.logger = logger ?? NullLogger.Instance;
        this.bandwidthLimiter = bandwidthLimiter;
        this.limiter = limiter ?? ImportConcurrencyLimiter.Shared;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var originalUrl = request.RequestUri?.AbsoluteUri
            ?? throw new InvalidOperationException("Download request URL is missing.");
        var candidates = MinecraftDownloadSourceResolver.EnumerateRequests(originalUrl, preference).ToList();

        for (var index = 0; index < candidates.Count; index++)
        {
            var resolution = candidates[index];
            var clonedRequest = await CloneRequestAsync(request, resolution.ActualUrl, cancellationToken);
            var lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await base.SendAsync(clonedRequest, cancellationToken);
                if (response.IsSuccessStatusCode || index == candidates.Count - 1)
                {
                    await DownloadResponseThrottler.ApplyAsync(response, bandwidthLimiter, cancellationToken, lease).ConfigureAwait(false);
                    logger.LogInformation(
                        "HTTP download routed. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} StatusCode={StatusCode} FallbackUsed={FallbackUsed}",
                        preference,
                        resolution.ResourceCategory,
                        resolution.OriginalUrl,
                        resolution.ActualUrl,
                        resolution.ResolvedSourceKind,
                        (int)response.StatusCode,
                        index > 0);
                    return response;
                }

                response.Dispose();
                await lease.DisposeAsync().ConfigureAwait(false);
                logger.LogWarning(
                    "HTTP download route failed and will fall back. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind} StatusCode={StatusCode}",
                    preference,
                    resolution.ResourceCategory,
                    resolution.OriginalUrl,
                    resolution.ActualUrl,
                    resolution.ResolvedSourceKind,
                    (int)response.StatusCode);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && index < candidates.Count - 1)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                logger.LogWarning(
                    exception,
                    "HTTP download route threw before fallback. RequestedSourcePreference={RequestedSourcePreference} ResourceCategory={ResourceCategory} OriginalUrl={OriginalUrl} ActualUrl={ActualUrl} ResolvedSourceKind={ResolvedSourceKind}",
                    preference,
                    resolution.ResourceCategory,
                    resolution.OriginalUrl,
                    resolution.ActualUrl,
                    resolution.ResolvedSourceKind);
            }
        }

        throw new InvalidOperationException($"Download route could not be resolved for {originalUrl}.");
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

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        string actualUrl,
        CancellationToken cancellationToken)
    {
        var clonedRequest = new HttpRequestMessage(request.Method, actualUrl)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            clonedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is null)
            return clonedRequest;

        var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var clonedContent = new ByteArrayContent(contentBytes);
        foreach (var header in request.Content.Headers)
            clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

        clonedRequest.Content = clonedContent;
        return clonedRequest;
    }
}
