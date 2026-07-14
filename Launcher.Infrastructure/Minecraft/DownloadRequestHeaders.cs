/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net.Http;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// A request header that may only be emitted to one exact origin.  It is kept
/// separate from ordinary request configuration so redirects cannot accidentally
/// carry credentials to a CDN or another host.
/// </summary>
internal sealed record DownloadRequestHeaders(Uri AllowedOrigin, IReadOnlyDictionary<string, string> Values)
{
    public static DownloadRequestHeaders CurseForgeApiKey(string apiKey)
    {
        return new DownloadRequestHeaders(
            new Uri("https://api.curseforge.com"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-api-key"] = apiKey
            });
    }

    public void ApplyIfAllowed(HttpRequestMessage request)
    {
        if (!SameOrigin(request.RequestUri, AllowedOrigin))
            return;

        foreach (var (name, value) in Values)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private static bool SameOrigin(Uri? left, Uri right)
    {
        return left is not null
            && string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;
    }
}
