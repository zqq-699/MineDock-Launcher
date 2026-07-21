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

    [Theory]
    [InlineData("[MOTD]World[/MOTD]")]
    [InlineData("[MOTD]World[/MOTD][AD]65536[/AD]")]
    public void TryParseAnnouncement_RejectsMalformedAnnouncements(string announcement)
    {
        var parsed = MinecraftLanWorldDiscoveryService.TryParseAnnouncement(
            Encoding.UTF8.GetBytes(announcement),
            out _,
            out _);

        Assert.False(parsed);
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
