using System.Net;
using System.Text;
using Launcher.Application.Services;
using Launcher.Infrastructure.Multiplayer;

namespace Launcher.Tests.Infrastructure.Multiplayer;

public sealed class MinecraftLanWorldDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverLocalWorldsAsync_ReportsNewWorldsWhileReceiving()
    {
        var localAddress = IPAddress.Parse("192.168.10.5");
        var receiver = new FakeDatagramReceiver(
        [
            CreateDatagram(localAddress, "First World", 51234),
            CreateDatagram(localAddress, "Duplicate World", 51234),
            CreateDatagram(localAddress, "Second World", 52345)
        ]);
        var progress = new RecordingProgress<MinecraftLanWorld>();
        var service = new MinecraftLanWorldDiscoveryService(
            receiver,
            new FakeLocalIpv4AddressProvider([localAddress]));

        var worlds = await service.DiscoverLocalWorldsAsync(progress: progress);

        Assert.Equal([51234, 52345], progress.Values.Select(world => world.Port));
        Assert.Equal([51234, 52345], worlds.Select(world => world.Port));
        Assert.Equal(MinecraftLanWorldDiscoveryService.DiscoveryWindow, receiver.Duration);
        Assert.Equal(MinecraftLanWorldDiscoveryService.DiscoverySettleWindow, receiver.SettleDuration);
    }

    [Fact]
    public void TryParseAnnouncement_ParsesPortAndSanitizesWorldName()
    {
        var payload = Encoding.UTF8.GetBytes("[MOTD]§aMy\u0001 World[/MOTD][AD]54321[/AD]");

        var parsed = MinecraftLanWorldDiscoveryService.TryParseAnnouncement(
            payload,
            out var worldName,
            out var port);

        Assert.True(parsed);
        Assert.Equal("My World", worldName);
        Assert.Equal(54321, port);
    }

    [Theory]
    [InlineData("[MOTD]World[/MOTD]")]
    [InlineData("[AD]25565[/AD]")]
    [InlineData("[MOTD]World[/MOTD][AD]0[/AD]")]
    [InlineData("[MOTD]World[/MOTD][AD]65536[/AD]")]
    [InlineData("[MOTD]World[/MOTD][AD]not-a-port[/AD]")]
    public void TryParseAnnouncement_RejectsMalformedAnnouncements(string announcement)
    {
        var parsed = MinecraftLanWorldDiscoveryService.TryParseAnnouncement(
            Encoding.UTF8.GetBytes(announcement),
            out _,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void ParseLocalWorlds_FiltersRemoteAddressesAndDeduplicatesPorts()
    {
        var localAddress = IPAddress.Parse("192.168.10.5");
        var secondLocalAddress = IPAddress.Parse("10.0.0.5");
        var remoteAddress = IPAddress.Parse("192.168.10.20");
        var datagrams = new[]
        {
            CreateDatagram(localAddress, "Local World", 52345),
            CreateDatagram(secondLocalAddress, "Duplicate World", 52345),
            CreateDatagram(remoteAddress, "Remote World", 60000),
            new MinecraftLanDatagram(localAddress, Encoding.UTF8.GetBytes("invalid"))
        };

        var worlds = MinecraftLanWorldDiscoveryService.ParseLocalWorlds(
            datagrams,
            [localAddress, secondLocalAddress]);

        var world = Assert.Single(worlds);
        Assert.Equal("Local World", world.Name);
        Assert.Equal(localAddress.ToString(), world.HostAddress);
        Assert.Equal(52345, world.Port);
    }

    [Fact]
    public void ParseLocalWorlds_AcceptsLoopbackWithoutProviderEntry()
    {
        var worlds = MinecraftLanWorldDiscoveryService.ParseLocalWorlds(
            [CreateDatagram(IPAddress.Loopback, "Loopback World", 51234)],
            []);

        Assert.Equal(51234, Assert.Single(worlds).Port);
    }

    [Fact]
    public void DefaultMulticastRouteAddress_IsIncludedInLocalAddressDiscovery()
    {
        var interfaceAddress = IPAddress.Parse("192.168.88.4");
        var routeAddress = IPAddress.Parse("10.7.0.2");

        var addresses = LocalIpv4AddressProvider.IncludeDefaultMulticastRouteAddress(
            [interfaceAddress],
            routeAddress);

        Assert.Equal([interfaceAddress, routeAddress, IPAddress.Loopback], addresses);
    }

    private static MinecraftLanDatagram CreateDatagram(IPAddress address, string name, int port)
    {
        return new MinecraftLanDatagram(
            address,
            Encoding.UTF8.GetBytes($"[MOTD]{name}[/MOTD][AD]{port}[/AD]"));
    }

    private sealed class FakeDatagramReceiver(IReadOnlyList<MinecraftLanDatagram> datagrams)
        : IMinecraftLanDatagramReceiver
    {
        public TimeSpan Duration { get; private set; }

        public TimeSpan SettleDuration { get; private set; }

        public Task ReceiveAsync(
            IReadOnlyCollection<IPAddress> localAddresses,
            TimeSpan duration,
            TimeSpan settleDuration,
            Func<MinecraftLanDatagram, bool> tryAcceptDatagram,
            CancellationToken cancellationToken)
        {
            Duration = duration;
            SettleDuration = settleDuration;
            foreach (var datagram in datagrams)
                tryAcceptDatagram(datagram);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalIpv4AddressProvider(IReadOnlyCollection<IPAddress> addresses)
        : ILocalIpv4AddressProvider
    {
        public IReadOnlyCollection<IPAddress> GetLocalIpv4Addresses() => addresses;
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
