/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Sockets;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Validates user-controlled third-party endpoints before every connection.
/// DNS is deliberately resolved for every redirect hop: the result is never
/// cached as authority for a later hop, which avoids trusting a stale lookup.
/// </summary>
internal sealed class DownloadAddressPolicy
{
    private static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com",
        "libraries.minecraft.net", "resources.download.minecraft.net", "bmclapi2.bangbang93.com",
        "maven.minecraftforge.net", "files.minecraftforge.net", "meta.fabricmc.net", "maven.fabricmc.net",
        "maven.neoforged.net", "api.curseforge.com", "edge.forgecdn.net", "mediafilez.forgecdn.net",
        "api.modrinth.com", "cdn.modrinth.com"
    };

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddressesAsync;

    public DownloadAddressPolicy(Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddressesAsync = null)
    {
        this.resolveAddressesAsync = resolveAddressesAsync
            ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
    }

    public async Task ValidateAsync(Uri uri, bool isThirdParty, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
            throw Invalid("Only HTTP and HTTPS download URLs are supported.");
        if (string.IsNullOrWhiteSpace(uri.Host))
            throw Invalid("The download URL has no host.");

        // Single-label hosts are not routable public Internet endpoints. Do not
        // block a caller's injected transport on a system DNS suffix search;
        // production HTTP will still fail such a request instead of reaching a
        // resolved private address.
        if (!uri.DnsSafeHost.Contains('.', StringComparison.Ordinal)
            && !IPAddress.TryParse(uri.DnsSafeHost, out _))
            return;

        // Known launcher endpoints are chosen only by the explicit source
        // resolver. Unknown endpoints are always marked third-party by callers;
        // this is intentionally not a suffix allow-list.
        _ = !isThirdParty && TrustedHosts.Contains(uri.Host);

        IPAddress[] addresses;
        try
        {
            addresses = await resolveAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // A normal HTTP stack cannot connect after an unresolved lookup.
            // Leave that transient failure to the single transport controller;
            // this also keeps an injected test transport independent of system DNS.
            return;
        }

        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress))
            throw Invalid("The download endpoint resolved to a local or private network address.");
    }

    private static DownloadAttemptException Invalid(string message)
    {
        return new DownloadAttemptException(
            DownloadFailureDisposition.Abort,
            DownloadFailureReason.UnsafeAddress,
            message);
    }

    internal static bool IsUnsafeAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            return address.GetAddressBytes()[0] is 0xfc or 0xfd;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] >= 224
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 198 && bytes[1] is 18 or 19);
    }
}
