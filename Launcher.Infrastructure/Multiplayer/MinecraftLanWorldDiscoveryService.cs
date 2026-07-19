/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Multiplayer;

internal sealed partial class MinecraftLanWorldDiscoveryService : IMinecraftLanWorldDiscoveryService
{
    internal static readonly TimeSpan DiscoveryWindow = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan DiscoverySettleWindow = TimeSpan.FromSeconds(2);
    private const int MaximumWorldNameLength = 120;

    private readonly IMinecraftLanDatagramReceiver datagramReceiver;
    private readonly ILocalIpv4AddressProvider addressProvider;
    private readonly ILogger<MinecraftLanWorldDiscoveryService> logger;

    public MinecraftLanWorldDiscoveryService(
        ILogger<MinecraftLanWorldDiscoveryService>? logger = null)
        : this(new MinecraftLanDatagramReceiver(), new LocalIpv4AddressProvider(), logger)
    {
    }

    internal MinecraftLanWorldDiscoveryService(
        IMinecraftLanDatagramReceiver datagramReceiver,
        ILocalIpv4AddressProvider addressProvider,
        ILogger<MinecraftLanWorldDiscoveryService>? logger = null)
    {
        this.datagramReceiver = datagramReceiver;
        this.addressProvider = addressProvider;
        this.logger = logger ?? NullLogger<MinecraftLanWorldDiscoveryService>.Instance;
    }

    public async Task<IReadOnlyList<MinecraftLanWorld>> DiscoverLocalWorldsAsync(
        CancellationToken cancellationToken = default,
        IProgress<MinecraftLanWorld>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Minecraft LAN world discovery started.");
        try
        {
            var localAddresses = addressProvider.GetLocalIpv4Addresses();
            var allowedAddresses = CreateAllowedAddressSet(localAddresses);
            var worldsByPort = new Dictionary<int, MinecraftLanWorld>();
            await datagramReceiver
                .ReceiveAsync(
                    localAddresses,
                    DiscoveryWindow,
                    DiscoverySettleWindow,
                    datagram => TryAddLocalWorld(datagram, allowedAddresses, worldsByPort, progress),
                    cancellationToken)
                .ConfigureAwait(false);
            var worlds = OrderWorlds(worldsByPort.Values);
            logger.LogInformation(
                "Minecraft LAN world discovery completed. WorldCount={WorldCount} DurationMs={DurationMs}",
                worlds.Count,
                stopwatch.ElapsedMilliseconds);
            return worlds;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Minecraft LAN world discovery canceled.");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Minecraft LAN world discovery failed. DurationMs={DurationMs}",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    internal static IReadOnlyList<MinecraftLanWorld> ParseLocalWorlds(
        IReadOnlyList<MinecraftLanDatagram> datagrams,
        IReadOnlyCollection<IPAddress> localAddresses)
    {
        var allowedAddresses = CreateAllowedAddressSet(localAddresses);
        var worldsByPort = new Dictionary<int, MinecraftLanWorld>();

        foreach (var datagram in datagrams)
            TryAddLocalWorld(datagram, allowedAddresses, worldsByPort);

        return OrderWorlds(worldsByPort.Values);
    }

    private static HashSet<IPAddress> CreateAllowedAddressSet(
        IReadOnlyCollection<IPAddress> localAddresses)
    {
        return new HashSet<IPAddress>(localAddresses)
        {
            IPAddress.Loopback
        };
    }

    private static bool TryAddLocalWorld(
        MinecraftLanDatagram datagram,
        IReadOnlySet<IPAddress> allowedAddresses,
        IDictionary<int, MinecraftLanWorld> worldsByPort,
        IProgress<MinecraftLanWorld>? progress = null)
    {
        if (!allowedAddresses.Contains(datagram.SourceAddress)
            || !TryParseAnnouncement(datagram.Payload, out var worldName, out var port))
        {
            return false;
        }

        var world = new MinecraftLanWorld(worldName, datagram.SourceAddress.ToString(), port);
        if (!worldsByPort.TryAdd(port, world))
            return false;

        progress?.Report(world);
        return true;
    }

    private static IReadOnlyList<MinecraftLanWorld> OrderWorlds(
        IEnumerable<MinecraftLanWorld> worlds)
    {
        return worlds
            .OrderBy(world => world.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(world => world.Port)
            .ToArray();
    }

    internal static bool TryParseAnnouncement(
        ReadOnlyMemory<byte> payload,
        out string worldName,
        out int port)
    {
        worldName = string.Empty;
        port = 0;
        if (payload.IsEmpty)
            return false;

        var announcement = Encoding.UTF8.GetString(payload.Span);
        var motdMatch = MotdPattern().Match(announcement);
        var portMatch = PortPattern().Match(announcement);
        if (!motdMatch.Success
            || !portMatch.Success
            || !int.TryParse(portMatch.Groups[1].Value, out port)
            || port is < 1 or > IPEndPoint.MaxPort)
        {
            port = 0;
            return false;
        }

        worldName = SanitizeWorldName(motdMatch.Groups[1].Value);
        return true;
    }

    private static string SanitizeWorldName(string value)
    {
        var withoutFormatting = MinecraftFormattingPattern().Replace(value, string.Empty);
        var cleaned = new string(withoutFormatting.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return cleaned.Length <= MaximumWorldNameLength
            ? cleaned
            : cleaned[..MaximumWorldNameLength];
    }

    [GeneratedRegex(@"\[MOTD\](.*?)\[/MOTD\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MotdPattern();

    [GeneratedRegex(@"\[AD\](\d+)\[/AD\]", RegexOptions.CultureInvariant)]
    private static partial Regex PortPattern();

    [GeneratedRegex("§[0-9A-FK-OR]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MinecraftFormattingPattern();
}

internal sealed record MinecraftLanDatagram(IPAddress SourceAddress, ReadOnlyMemory<byte> Payload);

internal interface IMinecraftLanDatagramReceiver
{
    Task ReceiveAsync(
        IReadOnlyCollection<IPAddress> localAddresses,
        TimeSpan duration,
        TimeSpan settleDuration,
        Func<MinecraftLanDatagram, bool> tryAcceptDatagram,
        CancellationToken cancellationToken);
}

internal sealed class MinecraftLanDatagramReceiver : IMinecraftLanDatagramReceiver
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.2.60");
    private const int MulticastPort = 4445;

    public async Task ReceiveAsync(
        IReadOnlyCollection<IPAddress> localAddresses,
        TimeSpan duration,
        TimeSpan settleDuration,
        Func<MinecraftLanDatagram, bool> tryAcceptDatagram,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.MulticastLoopback = true;
        client.Client.ExclusiveAddressUse = false;
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));

        var joinedGroup = false;
        foreach (var address in localAddresses.Where(address =>
                     address.AddressFamily == AddressFamily.InterNetwork
                     && !IPAddress.IsLoopback(address)))
        {
            try
            {
                client.JoinMulticastGroup(MulticastAddress, address);
                joinedGroup = true;
            }
            catch (SocketException)
            {
                // Another active interface may still be able to receive the announcement.
            }
        }

        if (!joinedGroup)
            client.JoinMulticastGroup(MulticastAddress);

        var stopwatch = Stopwatch.StartNew();
        TimeSpan? lastAcceptedAt = null;
        while (true)
        {
            var remaining = duration - stopwatch.Elapsed;
            if (lastAcceptedAt is { } acceptedAt)
            {
                var settleRemaining = settleDuration - (stopwatch.Elapsed - acceptedAt);
                if (settleRemaining < remaining)
                    remaining = settleRemaining;
            }

            if (remaining <= TimeSpan.Zero)
                break;

            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(remaining);
            try
            {
                var result = await client.ReceiveAsync(receiveTimeout.Token).ConfigureAwait(false);
                var datagram = new MinecraftLanDatagram(result.RemoteEndPoint.Address, result.Buffer);
                if (tryAcceptDatagram(datagram))
                    lastAcceptedAt = stopwatch.Elapsed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

internal interface ILocalIpv4AddressProvider
{
    IReadOnlyCollection<IPAddress> GetLocalIpv4Addresses();
}

internal sealed class LocalIpv4AddressProvider : ILocalIpv4AddressProvider
{
    public IReadOnlyCollection<IPAddress> GetLocalIpv4Addresses()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToList();
        if (!addresses.Contains(IPAddress.Loopback))
            addresses.Add(IPAddress.Loopback);
        return addresses;
    }
}
