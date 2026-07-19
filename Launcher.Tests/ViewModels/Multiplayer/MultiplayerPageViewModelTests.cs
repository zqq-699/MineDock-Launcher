/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Multiplayer;

public sealed class MultiplayerPageViewModelTests
{
    [Fact]
    public void InitialStatePromptsRefreshAndCannotCreate()
    {
        var viewModel = CreateViewModel(new FakeLanWorldDiscoveryService([]));

        Assert.Empty(viewModel.LanWorlds);
        Assert.False(viewModel.HasLanWorlds);
        Assert.Equal(Strings.Multiplayer_Create_LanWorldRefreshHint, viewModel.LanWorldDiscoveryStatus);
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshDisplaysDetectedWorldAndEnablesCreate()
    {
        var world = new MinecraftLanWorld("New World", "127.0.0.1", 11748);
        var viewModel = CreateViewModel(new FakeLanWorldDiscoveryService([world]));

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        Assert.Equal("New World · Port 11748", Assert.Single(viewModel.LanWorlds).DisplayText);
        Assert.True(viewModel.HasLanWorlds);
        Assert.True(viewModel.CreateLobbyCommand.CanExecute(null));
        Assert.Empty(viewModel.LanWorldDiscoveryStatus);
    }

    [Fact]
    public async Task MultipleWorldsAreAllDisplayedWithTerracottaWarning()
    {
        var discovery = new FakeLanWorldDiscoveryService(
        [
            new MinecraftLanWorld("One", "127.0.0.1", 11111),
            new MinecraftLanWorld("Two", "127.0.0.1", 22222)
        ]);
        var viewModel = CreateViewModel(discovery);

        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.LanWorlds.Count);
        Assert.True(viewModel.HasMultipleLanWorlds);
        Assert.Equal(
            Strings.Multiplayer_Create_MultipleLanWorldsWarning,
            viewModel.LanWorldDiscoveryStatus);
    }

    [Fact]
    public async Task RefreshBusyStateDisablesRefreshAndCreate()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<MinecraftLanWorld>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new FakeLanWorldDiscoveryService([]) { PendingResult = completion.Task };
        var viewModel = CreateViewModel(discovery);

        var refresh = viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.IsRefreshingLanWorlds);

        Assert.Equal(Strings.Multiplayer_Create_LanWorldDiscovering, viewModel.LanWorldDiscoveryStatus);
        Assert.False(viewModel.RefreshLanWorldsCommand.CanExecute(null));
        Assert.False(viewModel.CreateLobbyCommand.CanExecute(null));

        completion.SetResult([]);
        await refresh;
    }

    [Fact]
    public async Task CreateShowsBusyTextAndSwitchesOnlyAfterTerracottaSucceeds()
    {
        var completion = new TaskCompletionSource<MultiplayerLobbySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lobby = new FakeMultiplayerLobbyService { PendingCreate = completion.Task };
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService(
                [new MinecraftLanWorld("World", "127.0.0.1", 11748)]),
            lobby);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        var create = viewModel.CreateLobbyCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.IsCreatingLobby);

        Assert.Equal(Strings.Multiplayer_Create_CreatingLobby, viewModel.CreateLobbyButtonText);
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.False(viewModel.RefreshLanWorldsCommand.CanExecute(null));

        completion.SetResult(FakeMultiplayerLobbyService.CreateSnapshot("Host"));
        await create;

        Assert.Equal(MultiplayerCreateLobbyStep.Lobby, viewModel.CreateLobbyStep);
        Assert.Equal("U/1234-5678-9ABC-DEFG", viewModel.RoomCode);
        Assert.Equal(1, lobby.CreateCount);
    }

    [Fact]
    public async Task CreationFailureKeepsDetectedWorldAndReportsFriendlyMessage()
    {
        var messages = new RecordingMessageService();
        var lobby = new FakeMultiplayerLobbyService
        {
            CreateException = new MultiplayerLobbyCreationException(
                MultiplayerLobbyCreationFailure.TerracottaBusy,
                "technical")
        };
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService(
                [new MinecraftLanWorld("World", "127.0.0.1", 11748)]),
            lobby,
            messageService: messages);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);

        await viewModel.CreateLobbyCommand.ExecuteAsync(null);

        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.Single(viewModel.LanWorlds);
        Assert.Contains(Strings.Multiplayer_Create_TerracottaBusy, messages.Messages);
        Assert.DoesNotContain("technical", messages.Messages);
    }

    [Fact]
    public async Task SnapshotAndStopEventsUpdateRealLobbyState()
    {
        var messages = new RecordingMessageService();
        var lobby = new FakeMultiplayerLobbyService();
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService(
                [new MinecraftLanWorld("World", "127.0.0.1", 11748)]),
            lobby,
            messageService: messages);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        await viewModel.CreateLobbyCommand.ExecuteAsync(null);

        lobby.RaiseSnapshot(new MultiplayerLobbySnapshot(
            "U/REAL-CODE",
            MultiplayerLobbyState.Active,
            [
                new MultiplayerLobbyPlayer(
                    "Host",
                    "host",
                    "Terracotta 0.4.2, EasyTier v2.5.0-terracotta.2",
                    MultiplayerLobbyPlayerKind.Host),
                new MultiplayerLobbyPlayer(
                    "Guest",
                    "guest",
                    "PCL CE 2.15.0-beta.6, EasyTier 2.5.0",
                    MultiplayerLobbyPlayerKind.Guest)
            ]));

        Assert.Equal("U/REAL-CODE", viewModel.RoomCode);
        Assert.Equal(2, viewModel.LobbyPlayers.Count);
        Assert.Equal(
            "Terracotta 0.4.2, EasyTier v2.5.0-terracotta.2",
            viewModel.LobbyPlayers[0].Subtitle);
        Assert.Equal(
            "PCL CE 2.15.0-beta.6, EasyTier 2.5.0",
            viewModel.LobbyPlayers[1].Subtitle);
        Assert.Equal([Strings.Multiplayer_LobbyPlayerRoleHost], viewModel.LobbyPlayers[0].RoleTags);
        Assert.Equal([Strings.Multiplayer_LobbyPlayerRolePlayer], viewModel.LobbyPlayers[1].RoleTags);

        lobby.RaiseStopped(MultiplayerLobbyStopReason.MinecraftWorldClosed);

        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.Contains(Strings.Multiplayer_LobbyWorldClosed, messages.Messages);
    }

    [Fact]
    public async Task CopyAndDisbandUseRealLobbyService()
    {
        var lobby = new FakeMultiplayerLobbyService();
        var clipboard = new RecordingClipboardService();
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService(
                [new MinecraftLanWorld("World", "127.0.0.1", 11748)]),
            lobby,
            clipboard);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        await viewModel.CreateLobbyCommand.ExecuteAsync(null);

        viewModel.CopyRoomCodeCommand.Execute(null);
        viewModel.RequestLeaveLobbyCommand.Execute(null);
        await viewModel.ConfirmLeaveLobbyCommand.ExecuteAsync(null);

        Assert.Equal("U/1234-5678-9ABC-DEFG", clipboard.Text);
        Assert.Equal(1, lobby.StopCount);
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
    }

    private static MultiplayerPageViewModel CreateViewModel(
        IMinecraftLanWorldDiscoveryService discoveryService,
        FakeMultiplayerLobbyService? lobbyService = null,
        RecordingClipboardService? clipboardService = null,
        RecordingMessageService? messageService = null)
    {
        var messages = messageService ?? new RecordingMessageService();
        return new MultiplayerPageViewModel(
            discoveryService,
            lobbyService ?? new FakeMultiplayerLobbyService(),
            clipboardService ?? new RecordingClipboardService(),
            ImmediateUiDispatcher.Instance,
            messages,
            messages);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeLanWorldDiscoveryService(IReadOnlyList<MinecraftLanWorld> worlds)
        : IMinecraftLanWorldDiscoveryService
    {
        public Task<IReadOnlyList<MinecraftLanWorld>>? PendingResult { get; set; }

        public Task<IReadOnlyList<MinecraftLanWorld>> DiscoverLocalWorldsAsync(
            CancellationToken cancellationToken = default,
            IProgress<MinecraftLanWorld>? progress = null) =>
            PendingResult ?? Task.FromResult(worlds);
    }

    private sealed class FakeMultiplayerLobbyService : IMultiplayerLobbyService
    {
        public MultiplayerLobbySnapshot? Current { get; private set; }
        public Task<MultiplayerLobbySnapshot>? PendingCreate { get; set; }
        public Exception? CreateException { get; set; }
        public int CreateCount { get; private set; }
        public int StopCount { get; private set; }

        public event Action<MultiplayerLobbySnapshot>? SnapshotChanged;
        public event Action<MultiplayerLobbyStopped>? Stopped;

        public Task<MultiplayerLobbySnapshot> CreateHostAsync(
            string hostName,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            if (CreateException is not null)
                return Task.FromException<MultiplayerLobbySnapshot>(CreateException);
            if (PendingCreate is not null)
                return PendingCreate;
            Current = CreateSnapshot(hostName);
            return Task.FromResult(Current);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            Current = null;
            return Task.CompletedTask;
        }

        public void RaiseSnapshot(MultiplayerLobbySnapshot snapshot)
        {
            Current = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }

        public void RaiseStopped(MultiplayerLobbyStopReason reason)
        {
            Current = null;
            Stopped?.Invoke(new MultiplayerLobbyStopped(reason));
        }

        public static MultiplayerLobbySnapshot CreateSnapshot(string hostName) => new(
            "U/1234-5678-9ABC-DEFG",
            MultiplayerLobbyState.Active,
            [new MultiplayerLobbyPlayer(
                hostName,
                "host-machine",
                "Terracotta",
                MultiplayerLobbyPlayerKind.Host)]);
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? Text { get; private set; }
        public void CopyText(string text) => Text = text;
    }

    private sealed class RecordingMessageService : IStatusService, IFloatingMessageService
    {
        public event Action<string>? MessageReported;
        public event Action<string>? MessageRequested;
        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
            MessageReported?.Invoke(message);
        }

        public void Show(string message)
        {
            Messages.Add(message);
            MessageRequested?.Invoke(message);
        }
    }
}
