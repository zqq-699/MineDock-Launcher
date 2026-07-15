/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Sockets;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record ValidatedDownloadEndpoint(
    Uri Uri,
    bool RequiresDirectConnection,
    IReadOnlyList<IPAddress> Addresses);

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
        "meta.quiltmc.org", "maven.quiltmc.org", "maven.neoforged.net", "api.curseforge.com",
        "edge.forgecdn.net", "mediafilez.forgecdn.net",
        "api.modrinth.com", "cdn.modrinth.com", "authlib-injector.yushi.moe"
    };

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddressesAsync;

    public DownloadAddressPolicy(Func<string, CancellationToken, Task<IPAddress[]>>? resolveAddressesAsync = null)
    {
        this.resolveAddressesAsync = resolveAddressesAsync
            ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
    }

    internal static DownloadAddressPolicy CreateForInjectedTransport() => new(
        static (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }));

    public async Task<ValidatedDownloadEndpoint> ValidateAsync(
        Uri uri,
        bool isThirdParty,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Download URLs must use HTTPS.");
        if (string.IsNullOrWhiteSpace(uri.Host))
            throw Invalid("The download URL has no host.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw Invalid("Download URLs must not contain user information.");

        var requiresDirectConnection = isThirdParty || !TrustedHosts.Contains(uri.DnsSafeHost);
        if (!requiresDirectConnection)
        {
            return new ValidatedDownloadEndpoint(
                uri,
                RequiresDirectConnection: false,
                Addresses: Array.Empty<IPAddress>());
        }

        if (!uri.DnsSafeHost.Contains('.', StringComparison.Ordinal)
            && !IPAddress.TryParse(uri.DnsSafeHost, out _))
        {
            throw Invalid("Single-label download hosts are not allowed.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            try
            {
                addresses = await resolveAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                throw Invalid("The download endpoint could not be resolved.", exception);
            }
        }

        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress))
            throw Invalid("The download endpoint resolved to a local or private network address.");

        return new ValidatedDownloadEndpoint(
            uri,
            RequiresDirectConnection: true,
            addresses.Distinct().ToArray());
    }

    private static DownloadAttemptException Invalid(string message, Exception? innerException = null)
    {
        return new DownloadAttemptException(
            DownloadFailureDisposition.Abort,
            DownloadFailureReason.UnsafeAddress,
            message,
            innerException);
    }

    internal static bool IsUnsafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return IsUnsafeAddress(address.MapToIPv4());
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            var ipv6 = address.GetAddressBytes();
            return ipv6[0] is 0xfc or 0xfd
                || ipv6.AsSpan(0, 4).SequenceEqual(new byte[] { 0x20, 0x01, 0x0d, 0xb8 });
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] >= 224
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 198 && bytes[1] is 18 or 19)
            || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}
