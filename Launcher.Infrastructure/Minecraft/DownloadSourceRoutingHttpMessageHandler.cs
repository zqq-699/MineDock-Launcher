using System.Net.Http;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadSourceRoutingHttpMessageHandler : DelegatingHandler
{
    private readonly DownloadSourcePreference preference;
    private readonly ILogger logger;
    private readonly DownloadBandwidthLimiter? bandwidthLimiter;

    public DownloadSourceRoutingHttpMessageHandler(
        DownloadSourcePreference preference,
        HttpMessageHandler innerHandler,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null)
        : base(innerHandler)
    {
        this.preference = preference;
        this.logger = logger ?? NullLogger.Instance;
        this.bandwidthLimiter = bandwidthLimiter;
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
            try
            {
                var response = await base.SendAsync(clonedRequest, cancellationToken);
                if (response.IsSuccessStatusCode || index == candidates.Count - 1)
                {
                    await DownloadResponseThrottler.ApplyAsync(response, bandwidthLimiter, cancellationToken);
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
