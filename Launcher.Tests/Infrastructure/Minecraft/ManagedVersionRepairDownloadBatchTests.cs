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
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ManagedVersionRepairDownloadBatchTests : TestTempDirectory
{
    [Fact]
    public async Task DuplicateDestinationIsDownloadedOnce()
    {
        var handler = new CountingHandler();
        var destination = Path.Combine(TempRoot, "libraries", "example.jar");
        var batch = CreateBatch(handler);
        var request = new RepairDownloadRequest(
            "https://example.test/example.jar",
            destination,
            "Mojang",
            "example",
            "example.jar");

        await batch.DownloadAllAsync([request, request], CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("content", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task PreCanceledBatchDoesNotStartRequests()
    {
        var handler = new CountingHandler();
        var batch = CreateBatch(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch.DownloadAllAsync(
            [new RepairDownloadRequest(
                "https://example.test/example.jar",
                Path.Combine(TempRoot, "example.jar"),
                "Mojang",
                null,
                "example.jar")],
            cancellation.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    private static ManagedVersionRepairDownloadBatch CreateBatch(HttpMessageHandler handler)
    {
        return new ManagedVersionRepairDownloadBatch(
            new HttpClient(handler),
            downloadSpeedLimitState: null,
            NullLogger.Instance,
            DownloadSourcePreference.Official,
            speedLimitMbPerSecond: 0,
            progress: null);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("content")
            });
        }
    }
}
