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

using System.Net;
using System.Net.Http;
using System.Diagnostics;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class MinecraftDownloadTransport
{
    private readonly HttpClient httpClient;
    private readonly DownloadRetryOptions retryOptions;
    private readonly DownloadAddressPolicy addressPolicy;

    public MinecraftDownloadTransport(
        HttpClient httpClient,
        DownloadRetryOptions retryOptions,
        DownloadAddressPolicy? addressPolicy = null)
    {
        this.httpClient = httpClient;
        this.retryOptions = retryOptions;
        this.addressPolicy = addressPolicy ?? new DownloadAddressPolicy();
    }

    public async Task<DownloadTransportResult> SendAsync(
        string actualUrl,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configureRequest = null,
        DownloadRequestHeaders? sensitiveHeaders = null,
        bool isThirdParty = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(retryOptions.ResponseHeadersTimeout);

        var originalUri = new Uri(actualUrl, UriKind.Absolute);
        var currentUri = originalUri;
        var stopwatch = Stopwatch.StartNew();
        var redirects = new List<Uri>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            currentUri.AbsoluteUri
        };

        for (var redirectCount = 0; ; redirectCount++)
        {
            var endpoint = await addressPolicy.ValidateAsync(currentUri, isThirdParty, cancellationToken).ConfigureAwait(false);
            var response = await SendSingleAsync(
                endpoint,
                timeout.Token,
                cancellationToken,
                configureRequest,
                sensitiveHeaders)
                .ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
                return new DownloadTransportResult(response, originalUri, currentUri, currentUri.Host, redirects, stopwatch.Elapsed);

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

            if (!string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !visited.Add(nextUri.AbsoluteUri))
            {
                response.Dispose();
                throw InvalidRedirect("The HTTP redirect target was unsupported or formed a loop.");
            }

            response.Dispose();
            redirects.Add(nextUri);
            currentUri = nextUri;
        }
    }

    private async Task<HttpResponseMessage> SendSingleAsync(
        ValidatedDownloadEndpoint endpoint,
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        Action<HttpRequestMessage>? configureRequest,
        DownloadRequestHeaders? sensitiveHeaders)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Uri);
            configureRequest?.Invoke(request);
            if (request.RequestUri != endpoint.Uri)
            {
                throw new DownloadAttemptException(
                    DownloadFailureDisposition.Abort,
                    DownloadFailureReason.UnsafeAddress,
                    "The validated download request URI was changed before it was sent.");
            }
            if (endpoint.RequiresDirectConnection)
            {
                request.Options.Set(
                    DownloadConnectionRequestOptions.ValidatedAddresses,
                    endpoint.Addresses.ToArray());
            }
            sensitiveHeaders?.ApplyIfAllowed(request);
            return await httpClient.SendAsync(
                request,
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
