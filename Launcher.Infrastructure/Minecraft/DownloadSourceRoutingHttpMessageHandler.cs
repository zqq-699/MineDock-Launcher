using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class DownloadSourceRoutingHttpMessageHandler : DelegatingHandler
{
    private readonly HttpClient transportClient;
    private readonly MinecraftDownloadRequestExecutor executor;
    private readonly DownloadSourcePreference preference;

    public DownloadSourceRoutingHttpMessageHandler(
        DownloadSourcePreference preference,
        DownloadConcurrencyCategory category,
        HttpMessageHandler innerHandler,
        ILogger? logger = null,
        DownloadBandwidthLimiter? bandwidthLimiter = null,
        IImportConcurrencyLimiter? limiter = null,
        DownloadRetryOptions? retryOptions = null)
        : base(innerHandler)
    {
        this.preference = preference;
        transportClient = new HttpClient(innerHandler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        executor = new MinecraftDownloadRequestExecutor(
            transportClient,
            logger ?? NullLogger.Instance,
            bandwidthLimiter,
            limiter,
            category,
            retryOptions);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var originalUrl = request.RequestUri?.AbsoluteUri
            ?? throw new InvalidOperationException("Download request URL is missing.");
        var buffered = await executor.ExecuteAsync(
            originalUrl,
            preference,
            categoryHint: null,
            async (context, token) =>
            {
                var bytes = await context.Response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                ValidateBufferedMetadata(originalUrl, bytes);
                return BufferedResponse.Capture(context.Response, bytes);
            },
            cancellationToken).ConfigureAwait(false);

        return buffered.CreateResponse(request);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            transportClient.Dispose();
        base.Dispose(disposing);
    }

    private static void ValidateBufferedMetadata(string originalUrl, byte[] bytes)
    {
        var firstContentByte = bytes.FirstOrDefault(value => !char.IsWhiteSpace((char)value));
        var path = new Uri(originalUrl, UriKind.Absolute).AbsolutePath;
        var expectsJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.Contains("version_manifest", StringComparison.OrdinalIgnoreCase);
        var expectsXml = path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        if (firstContentByte is (byte)'{' or (byte)'[' || expectsJson)
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    throw new DownloadContentValidationException(
                        "CmlLib metadata JSON has an invalid root value.");
                }

                if (path.Contains("version_manifest", StringComparison.OrdinalIgnoreCase)
                    && (!document.RootElement.TryGetProperty("versions", out var versions)
                        || versions.ValueKind is not JsonValueKind.Array))
                {
                    throw new DownloadContentValidationException(
                        "Minecraft version manifest is missing a versions array.");
                }
            }
            catch (DownloadContentValidationException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new DownloadContentValidationException(
                    "CmlLib metadata response is not valid JSON.",
                    exception);
            }

            return;
        }

        if (firstContentByte == (byte)'<' || expectsXml)
        {
            try
            {
                var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
                if (document.Root?.Name.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase) is true)
                {
                    throw new DownloadContentValidationException(
                        "CmlLib metadata endpoint returned an HTML document.");
                }
            }
            catch (DownloadContentValidationException)
            {
                throw;
            }
            catch (System.Xml.XmlException exception)
            {
                throw new DownloadContentValidationException(
                    "CmlLib metadata response is not valid XML.",
                    exception);
            }
        }
    }

    private sealed record BufferedResponse(
        HttpStatusCode StatusCode,
        string? ReasonPhrase,
        Version Version,
        IReadOnlyList<KeyValuePair<string, string[]>> Headers,
        IReadOnlyList<KeyValuePair<string, string[]>> ContentHeaders,
        byte[] Content)
    {
        public static BufferedResponse Capture(HttpResponseMessage response, byte[] content)
        {
            return new BufferedResponse(
                response.StatusCode,
                response.ReasonPhrase,
                response.Version,
                response.Headers.Select(header =>
                    new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray())).ToList(),
                response.Content.Headers.Select(header =>
                    new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray())).ToList(),
                content);
        }

        public HttpResponseMessage CreateResponse(HttpRequestMessage request)
        {
            var content = new ByteArrayContent(Content);
            foreach (var header in ContentHeaders)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);

            var response = new HttpResponseMessage(StatusCode)
            {
                ReasonPhrase = ReasonPhrase,
                Version = Version,
                RequestMessage = request,
                Content = content
            };
            foreach (var header in Headers)
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return response;
        }
    }
}
