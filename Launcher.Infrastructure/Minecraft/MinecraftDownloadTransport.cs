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
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configureRequest = null)
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
            var response = await SendSingleAsync(currentUri, timeout.Token, cancellationToken, configureRequest)
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
        CancellationToken callerToken,
        Action<HttpRequestMessage>? configureRequest)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            configureRequest?.Invoke(request);
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
