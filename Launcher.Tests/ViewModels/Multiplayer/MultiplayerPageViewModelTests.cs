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
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void ActiveLobbyBlocksSwitchingToOtherMultiplayerSection(
        int currentSectionIndex,
        int requestedSectionIndex)
    {
        var viewModel = CreateViewModel(new FakeLanWorldDiscoveryService([]));
        var currentSection = viewModel.Sections[currentSectionIndex];
        var requestedSection = viewModel.Sections[requestedSectionIndex];
        viewModel.SelectSectionCommand.Execute(currentSection);
        viewModel.CreateLobbyStep = MultiplayerCreateLobbyStep.Lobby;

        viewModel.SelectSectionCommand.Execute(currentSection);

        Assert.False(viewModel.IsLobbySectionSwitchBlockedDialogOpen);

        viewModel.SelectSectionCommand.Execute(requestedSection);

        Assert.Same(currentSection, viewModel.SelectedSection);
        Assert.True(currentSection.IsSelected);
        Assert.False(requestedSection.IsSelected);
        Assert.True(viewModel.IsLobbySectionSwitchBlockedDialogOpen);

        viewModel.CloseLobbySectionSwitchBlockedDialogCommand.Execute(null);

        Assert.False(viewModel.IsLobbySectionSwitchBlockedDialogOpen);
    }

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
        Assert.Empty(viewModel.LobbyPlayers[0].LocalTags);
        Assert.Empty(viewModel.LobbyPlayers[1].LocalTags);

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

        await viewModel.CopyRoomCodeCommand.ExecuteAsync(null);
        viewModel.RequestLeaveLobbyCommand.Execute(null);
        await viewModel.ConfirmLeaveLobbyCommand.ExecuteAsync(null);

        Assert.Equal("U/1234-5678-9ABC-DEFG", clipboard.Text);
        Assert.Equal(1, lobby.StopCount);
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
    }

    [Fact]
    public async Task CopyRoomCodeReportsFailureOnlyWhenClipboardWriteFails()
    {
        var clipboard = new RecordingClipboardService { CopyResult = false };
        var messages = new RecordingMessageService();
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService(
                [new MinecraftLanWorld("World", "127.0.0.1", 11748)]),
            clipboardService: clipboard,
            messageService: messages);
        await viewModel.RefreshLanWorldsCommand.ExecuteAsync(null);
        await viewModel.CreateLobbyCommand.ExecuteAsync(null);

        await viewModel.CopyRoomCodeCommand.ExecuteAsync(null);

        Assert.Contains(Strings.Multiplayer_LobbyRoomCodeCopyFailed, messages.Messages);
        Assert.DoesNotContain(Strings.Multiplayer_LobbyRoomCodeCopied, messages.Messages);
    }

    [Fact]
    public async Task PasteRoomCodeTrimsClipboardTextAndEnablesJoin()
    {
        var clipboard = new RecordingClipboardService { TextToRead = "  U/ROOM-CODE  " };
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService([]),
            clipboardService: clipboard);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections[1]);

        await viewModel.PasteRoomCodeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsJoinLobbySection);
        Assert.Equal("U/ROOM-CODE", viewModel.JoinRoomCode);
        Assert.True(viewModel.JoinLobbyCommand.CanExecute(null));
    }

    [Fact]
    public async Task JoinUsesRoomCodeAndSwitchesToSharedLobbyView()
    {
        var lobby = new FakeMultiplayerLobbyService();
        var viewModel = CreateViewModel(new FakeLanWorldDiscoveryService([]), lobby);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections[1]);
        viewModel.JoinRoomCode = "U/ROOM-CODE";

        await viewModel.JoinLobbyCommand.ExecuteAsync(null);

        Assert.Equal(1, lobby.JoinCount);
        Assert.Equal("U/ROOM-CODE", lobby.JoinedRoomCode);
        Assert.Equal(MultiplayerCreateLobbyStep.Lobby, viewModel.CreateLobbyStep);
        Assert.False(viewModel.IsLobbyHost);
        Assert.Equal(Strings.Multiplayer_LobbyLeaveButton, viewModel.LeaveLobbyButtonText);
        Assert.Equal("Host", viewModel.LobbyOwnerName);
        Assert.Equal([Strings.Multiplayer_LobbyPlayerRoleHost], viewModel.LobbyPlayers[0].RoleTags);
        Assert.Empty(viewModel.LobbyPlayers[0].LocalTags);
        Assert.Equal([Strings.Multiplayer_LobbyPlayerRolePlayer], viewModel.LobbyPlayers[1].RoleTags);
        Assert.Equal([Strings.Multiplayer_LobbyPlayerRoleSelf], viewModel.LobbyPlayers[1].LocalTags);
    }

    [Fact]
    public async Task InvalidJoinCodeKeepsJoinPageAndReportsFriendlyMessage()
    {
        var messages = new RecordingMessageService();
        var lobby = new FakeMultiplayerLobbyService
        {
            JoinException = new MultiplayerLobbyCreationException(
                MultiplayerLobbyCreationFailure.InvalidRoomCode,
                "technical")
        };
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService([]),
            lobby,
            messageService: messages);
        viewModel.SelectSectionCommand.Execute(viewModel.Sections[1]);
        viewModel.JoinRoomCode = "invalid";

        await viewModel.JoinLobbyCommand.ExecuteAsync(null);

        Assert.Equal(MultiplayerCreateLobbyStep.Setup, viewModel.CreateLobbyStep);
        Assert.Equal(Strings.Multiplayer_Join_InvalidRoomCode, viewModel.JoinLobbyStatus);
        Assert.Contains(Strings.Multiplayer_Join_InvalidRoomCode, messages.Messages);
        Assert.DoesNotContain("technical", messages.Messages);
    }

    [Fact]
    public void TerracottaProjectLinkUsesOfficialRepository()
    {
        var externalLinks = new RecordingExternalLinkService();
        var viewModel = CreateViewModel(
            new FakeLanWorldDiscoveryService([]),
            externalLinkService: externalLinks);

        viewModel.OpenTerracottaProjectCommand.Execute(null);

        Assert.Equal(TerracottaAgreementDialogViewModel.TerracottaProjectUrl, externalLinks.LastUrl);
    }

    private static MultiplayerPageViewModel CreateViewModel(
        IMinecraftLanWorldDiscoveryService discoveryService,
        FakeMultiplayerLobbyService? lobbyService = null,
        RecordingClipboardService? clipboardService = null,
        RecordingMessageService? messageService = null,
        IExternalLinkService? externalLinkService = null)
    {
        var messages = messageService ?? new RecordingMessageService();
        return new MultiplayerPageViewModel(
            discoveryService,
            lobbyService ?? new FakeMultiplayerLobbyService(),
            clipboardService ?? new RecordingClipboardService(),
            ImmediateUiDispatcher.Instance,
            messages,
            messages,
            externalLinkService: externalLinkService);
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
        public Exception? JoinException { get; set; }
        public int CreateCount { get; private set; }
        public int JoinCount { get; private set; }
        public string? JoinedRoomCode { get; private set; }
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

        public Task<MultiplayerLobbySnapshot> JoinAsync(
            string roomCode,
            string playerName,
            CancellationToken cancellationToken = default)
        {
            JoinCount++;
            JoinedRoomCode = roomCode;
            if (JoinException is not null)
                return Task.FromException<MultiplayerLobbySnapshot>(JoinException);
            Current = new MultiplayerLobbySnapshot(
                roomCode,
                MultiplayerLobbyState.Active,
                [
                    new MultiplayerLobbyPlayer(
                        "Host",
                        "host-machine",
                        "Terracotta",
                        MultiplayerLobbyPlayerKind.Host),
                    new MultiplayerLobbyPlayer(
                        playerName,
                        "guest-machine",
                        "BlockHelm",
                        MultiplayerLobbyPlayerKind.Guest,
                        IsLocal: true)
                ]);
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
                MultiplayerLobbyPlayerKind.Host,
                IsLocal: true)]);
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? Text { get; private set; }
        public string? TextToRead { get; set; }
        public bool CopyResult { get; set; } = true;
        public Task<bool> CopyTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.FromResult(CopyResult);
        }
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(TextToRead);
    }

    private sealed class RecordingExternalLinkService : IExternalLinkService
    {
        public string? LastUrl { get; private set; }

        public bool TryOpen(string url)
        {
            LastUrl = url;
            return true;
        }
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
