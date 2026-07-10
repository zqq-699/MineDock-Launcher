using System.Net;
using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class MinecraftDownloadTransport
{
    private readonly HttpClient httpClient;
    private readonly DownloadRetryOptions retryOptions;

    public MinecraftDownloadTransport(HttpClient httpClient, DownloadRetryOptions retryOptions)
    {
        this.httpClient = httpClient;
        this.retryOptions = retryOptions;
    }

    public async Task<HttpResponseMessage> SendAsync(
        string actualUrl,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(retryOptions.ResponseHeadersTimeout);

        var currentUri = new Uri(actualUrl, UriKind.Absolute);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentUri.AbsoluteUri
        };

        for (var redirectCount = 0; ; redirectCount++)
        {
            var response = await SendSingleAsync(currentUri, timeout.Token, cancellationToken)
                .ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
                return response;

            if (redirectCount >= retryOptions.MaxRedirects)
            {
                response.Dispose();
                throw InvalidRedirect(
                    $"The HTTP redirect chain exceeded {retryOptions.MaxRedirects} hops.");
            }

            var location = response.Headers.Location;
            if (location is null)
            {
                response.Dispose();
                throw InvalidRedirect("The HTTP redirect response did not contain a Location header.");
            }

            Uri nextUri;
            try
            {
                nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            }
            catch (UriFormatException exception)
            {
                response.Dispose();
                throw InvalidRedirect("The HTTP redirect target was invalid.", exception);
            }

            if (nextUri.Scheme is not ("http" or "https") || !visited.Add(nextUri.AbsoluteUri))
            {
                response.Dispose();
                throw InvalidRedirect("The HTTP redirect target was unsupported or formed a loop.");
            }

            response.Dispose();
            currentUri = nextUri;
        }
    }

    private async Task<HttpResponseMessage> SendSingleAsync(
        Uri uri,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            return await httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!callerToken.IsCancellationRequested)
        {
            throw new DownloadAttemptException(
                DownloadFailureDisposition.RetryCurrentSource,
                DownloadFailureReason.ResponseHeadersTimeout,
                $"The source did not return response headers within {retryOptions.ResponseHeadersTimeout}.",
                exception);
        }
        catch (HttpRequestException exception)
            when (exception.HttpRequestError is HttpRequestError.ConfigurationLimitExceeded)
        {
            throw InvalidRedirect(
                "The HTTP redirect chain was invalid or exceeded the transport limit.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DownloadAttemptException(
                DownloadFailureDisposition.RetryCurrentSource,
                DownloadFailureReason.Network,
                "The HTTP request failed before response headers were received.",
                exception);
        }
    }

    private static DownloadAttemptException InvalidRedirect(
        string message,
        Exception? innerException = null)
    {
        return new DownloadAttemptException(
            DownloadFailureDisposition.SwitchSource,
            DownloadFailureReason.InvalidRedirect,
            message,
            innerException);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var value = (int)statusCode;
        return value is >= 300 and <= 399;
    }
}
