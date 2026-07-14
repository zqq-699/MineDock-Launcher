/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record DownloadTransportResult(
    HttpResponseMessage Response,
    Uri OriginalUri,
    Uri FinalUri,
    string FinalHost,
    IReadOnlyList<Uri> RedirectChain,
    TimeSpan ResponseHeadersDuration)
{
    public int RedirectCount => RedirectChain.Count;
}
