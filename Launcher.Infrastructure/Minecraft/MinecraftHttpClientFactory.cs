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
using System.Net.Sockets;
using System.IO;
using System.Runtime.CompilerServices;

namespace Launcher.Infrastructure.Minecraft;

internal static class MinecraftHttpClientFactory
{
    private static readonly ConditionalWeakTable<HttpClient, object> TransportClients = new();

    public static HttpClient CreateTransportClient(StrictDownloadSocketConnector? connector = null)
    {
        var client = new HttpClient(new RoutingDownloadHttpMessageHandler(connector))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        TransportClients.Add(client, new object());
        return client;
    }

    public static HttpMessageHandler CreateTransportHandler(StrictDownloadSocketConnector? connector = null)
    {
        return new RoutingDownloadHttpMessageHandler(connector);
    }

    internal static bool IsTransportClient(HttpClient client) => TransportClients.TryGetValue(client, out _);
}

internal delegate ValueTask<Stream> StrictDownloadSocketConnector(
    IReadOnlyList<IPAddress> addresses,
    int port,
    CancellationToken cancellationToken);

internal static class DownloadConnectionRequestOptions
{
    public static readonly HttpRequestOptionsKey<IPAddress[]> ValidatedAddresses =
        new("BlockHelm.ValidatedDownloadAddresses");
}

internal sealed class RoutingDownloadHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker trustedInvoker;
    private readonly HttpMessageInvoker directInvoker;

    public RoutingDownloadHttpMessageHandler(StrictDownloadSocketConnector? connector = null)
    {
        trustedInvoker = new HttpMessageInvoker(new HttpClientHandler
        {
            AllowAutoRedirect = false
        }, disposeHandler: true);
        directInvoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!context.InitialRequestMessage.Options.TryGetValue(
                        DownloadConnectionRequestOptions.ValidatedAddresses,
                        out var addresses)
                    || addresses.Length == 0)
                {
                    throw new HttpRequestException("The strict download request had no validated addresses.");
                }

                return connector is null
                    ? await ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false)
                    : await connector(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            }
        }, disposeHandler: true);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var invoker = request.Options.TryGetValue(
            DownloadConnectionRequestOptions.ValidatedAddresses,
            out _)
            ? directInvoker
            : trustedInvoker;
        return invoker.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            directInvoker.Dispose();
            trustedInvoker.Dispose();
        }
        base.Dispose(disposing);
    }

    private static async ValueTask<Stream> ConnectAsync(
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;
                lastException = exception;
            }
        }

        throw new HttpRequestException("None of the validated download addresses could be reached.", lastException);
    }
}
