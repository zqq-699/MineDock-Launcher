/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed partial class MultiplayerPageViewModel : ObservableObject
{
    private readonly AccountPageViewModel? accountPage;
    private readonly IMinecraftLanWorldDiscoveryService lanWorldDiscoveryService;
    private readonly IMultiplayerLobbyService lobbyService;
    private readonly IClipboardService clipboardService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<MultiplayerPageViewModel> logger;

    [ObservableProperty]
    private MultiplayerSectionItem? selectedSection;

    [ObservableProperty]
    private MultiplayerCreateLobbyStep createLobbyStep;

    [ObservableProperty]
    private string lobbyOwnerName = Strings.Multiplayer_LobbyOwnerPlaceholder;

    [ObservableProperty]
    private string roomCode = string.Empty;

    [ObservableProperty]
    private bool isLeaveLobbyDialogOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLanWorldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateLobbyCommand))]
    private bool isRefreshingLanWorlds;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLanWorldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateLobbyCommand))]
    private bool isCreatingLobby;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestLeaveLobbyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLeaveLobbyCommand))]
    private bool isStoppingLobby;

    [ObservableProperty]
    private string lanWorldDiscoveryStatus = Strings.Multiplayer_Create_LanWorldRefreshHint;

    public MultiplayerPageViewModel(
        IMinecraftLanWorldDiscoveryService lanWorldDiscoveryService,
        IMultiplayerLobbyService lobbyService,
        IClipboardService clipboardService,
        IUiDispatcher uiDispatcher,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        AccountPageViewModel? accountPage = null,
        ILogger<MultiplayerPageViewModel>? logger = null)
    {
        this.lanWorldDiscoveryService = lanWorldDiscoveryService;
        this.lobbyService = lobbyService;
        this.clipboardService = clipboardService;
        this.uiDispatcher = uiDispatcher;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.accountPage = accountPage;
        this.logger = logger ?? NullLogger<MultiplayerPageViewModel>.Instance;
        Sections =
        [
            new(MultiplayerPageSection.CreateLobby, Strings.Multiplayer_SectionCreateLobby, "multiple_player/multi_create"),
            new(MultiplayerPageSection.JoinLobby, Strings.Multiplayer_SectionJoinLobby, "multiple_player/multi_enter")
        ];
        SelectedSection = Sections[0];
        lobbyService.SnapshotChanged += OnLobbySnapshotChanged;
        lobbyService.Stopped += OnLobbyStopped;
    }

    public ObservableCollection<MultiplayerSectionItem> Sections { get; }

    public ObservableCollection<MultiplayerLobbyPlayerItem> LobbyPlayers { get; } = [];

    public ObservableCollection<MultiplayerLanWorldItem> LanWorlds { get; } = [];

    public string SectionTitle => IsLobbyStep
        ? LobbyTitle
        : SelectedSection?.Title ?? Strings.Multiplayer_SectionCreateLobby;

    public bool IsCreateLobbySection => SelectedSection?.Section is MultiplayerPageSection.CreateLobby;

    public bool IsLobbyStep => CreateLobbyStep is MultiplayerCreateLobbyStep.Lobby;

    public string LobbyTitle => string.Format(Strings.Multiplayer_LobbyTitleFormat, LobbyOwnerName);

    public bool HasLanWorldDiscoveryStatus => !string.IsNullOrWhiteSpace(LanWorldDiscoveryStatus);

    public bool HasLanWorlds => LanWorlds.Count > 0;

    public bool HasMultipleLanWorlds => LanWorlds.Count > 1;

    public string CreateLobbyButtonText => IsCreatingLobby
        ? Strings.Multiplayer_Create_CreatingLobby
        : Strings.Multiplayer_SectionCreateLobby;

    private bool CanRefreshLanWorlds => !IsRefreshingLanWorlds && !IsCreatingLobby;

    private bool CanCreateLobby => LanWorlds.Count > 0
        && !IsRefreshingLanWorlds
        && !IsCreatingLobby;

    private bool CanRequestLeaveLobby => IsLobbyStep && !IsStoppingLobby;

    private bool CanConfirmLeaveLobby => IsLobbyStep && !IsStoppingLobby;

    private bool CanCopyRoomCode => !string.IsNullOrWhiteSpace(RoomCode);

    [RelayCommand]
    private void SelectSection(MultiplayerSectionItem? section)
    {
        if (section is not null)
            SelectedSection = section;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshLanWorlds))]
    private async Task RefreshLanWorldsAsync(CancellationToken cancellationToken)
    {
        IsRefreshingLanWorlds = true;
        LanWorldDiscoveryStatus = Strings.Multiplayer_Create_LanWorldDiscovering;
        var discoveryCompleted = false;
        try
        {
            var hasPublishedProgress = false;
            var progress = new Progress<MinecraftLanWorld>(world =>
            {
                if (discoveryCompleted)
                    return;

                if (!hasPublishedProgress)
                {
                    LanWorlds.Clear();
                    NotifyLanWorldsChanged();
                    hasPublishedProgress = true;
                }

                var item = CreateLanWorldItem(world);
                var existing = LanWorlds.FirstOrDefault(candidate => candidate.World.Port == world.Port);
                if (existing is not null)
                    LanWorlds[LanWorlds.IndexOf(existing)] = item;
                else
                    LanWorlds.Add(item);
                NotifyLanWorldsChanged();
            });
            var discoveredWorlds = await lanWorldDiscoveryService
                .DiscoverLocalWorldsAsync(cancellationToken, progress);
            discoveryCompleted = true;
            var items = discoveredWorlds.Select(CreateLanWorldItem).ToArray();

            LanWorlds.Clear();
            foreach (var item in items)
                LanWorlds.Add(item);
            NotifyLanWorldsChanged();
            LanWorldDiscoveryStatus = items.Length == 0
                ? Strings.Multiplayer_Create_LanWorldNotFound
                : items.Length > 1
                    ? Strings.Multiplayer_Create_MultipleLanWorldsWarning
                    : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LanWorldDiscoveryStatus = string.Empty;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to discover local Minecraft LAN worlds.");
            LanWorldDiscoveryStatus = Strings.Multiplayer_Create_LanWorldDiscoveryFailed;
        }
        finally
        {
            discoveryCompleted = true;
            IsRefreshingLanWorlds = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateLobby))]
    private async Task CreateLobbyAsync(CancellationToken cancellationToken)
    {
        if (LanWorlds.Count == 0)
            return;

        IsCreatingLobby = true;
        LanWorldDiscoveryStatus = string.Empty;
        try
        {
            var hostName = accountPage?.SelectedAccount?.DisplayName
                ?? Strings.Multiplayer_LobbyOwnerPlaceholder;
            var snapshot = await lobbyService.CreateHostAsync(hostName, cancellationToken);
            ApplyLobbySnapshot(snapshot);
            CreateLobbyStep = MultiplayerCreateLobbyStep.Lobby;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (MultiplayerLobbyCreationException exception)
        {
            logger.LogWarning(exception,
                "Failed to create multiplayer lobby. Failure={Failure}",
                exception.Failure);
            ReportFailure(MapCreationFailure(exception.Failure));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to create multiplayer lobby.");
            ReportFailure(Strings.Multiplayer_Create_LobbyFailed);
        }
        finally
        {
            IsCreatingLobby = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestLeaveLobby))]
    private void RequestLeaveLobby()
    {
        IsLeaveLobbyDialogOpen = true;
    }

    [RelayCommand]
    private void CancelLeaveLobby()
    {
        IsLeaveLobbyDialogOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmLeaveLobby))]
    private async Task ConfirmLeaveLobbyAsync(CancellationToken cancellationToken)
    {
        IsLeaveLobbyDialogOpen = false;
        IsStoppingLobby = true;
        try
        {
            await lobbyService.StopAsync(cancellationToken);
            ResetLobbyView();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to stop multiplayer lobby cleanly.");
            ReportFailure(Strings.Multiplayer_LobbyDisbandFailed);
        }
        finally
        {
            IsStoppingLobby = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyRoomCode))]
    private void CopyRoomCode()
    {
        clipboardService.CopyText(RoomCode);
        statusService.Report(Strings.Multiplayer_LobbyRoomCodeCopied);
        floatingMessageService.Show(Strings.Multiplayer_LobbyRoomCodeCopied);
    }

    private void OnLobbySnapshotChanged(MultiplayerLobbySnapshot snapshot)
    {
        uiDispatcher.Post(() => ApplyLobbySnapshot(snapshot));
    }

    private void OnLobbyStopped(MultiplayerLobbyStopped stopped)
    {
        uiDispatcher.Post(() =>
        {
            ResetLobbyView();
            var message = stopped.Reason switch
            {
                MultiplayerLobbyStopReason.MinecraftWorldClosed => Strings.Multiplayer_LobbyWorldClosed,
                MultiplayerLobbyStopReason.TerracottaExited => Strings.Multiplayer_LobbyTerracottaExited,
                MultiplayerLobbyStopReason.TerracottaServiceFailed => Strings.Multiplayer_LobbyTerracottaServiceFailed,
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(message))
                ReportFailure(message);
        });
    }

    private void ApplyLobbySnapshot(MultiplayerLobbySnapshot snapshot)
    {
        RoomCode = snapshot.RoomCode;
        LobbyOwnerName = snapshot.Players
            .FirstOrDefault(player => player.Kind is MultiplayerLobbyPlayerKind.Host)?.DisplayName
            ?? Strings.Multiplayer_LobbyOwnerPlaceholder;
        LobbyPlayers.Clear();
        for (var index = 0; index < snapshot.Players.Count; index++)
        {
            var player = snapshot.Players[index];
            LobbyPlayers.Add(new MultiplayerLobbyPlayerItem(
                player.DisplayName,
                player.Vendor,
                player.LatencyMilliseconds is { } latency
                    ? string.Format(Strings.Multiplayer_LobbyLatencyFormat, latency)
                    : Strings.Multiplayer_LobbyLatencyUnknown,
                player.Kind is MultiplayerLobbyPlayerKind.Host
                    ? Strings.Multiplayer_LobbyPlayerRoleHost
                    : Strings.Multiplayer_LobbyPlayerRolePlayer,
                player.Kind is MultiplayerLobbyPlayerKind.Host,
                index == 0,
                index == snapshot.Players.Count - 1));
        }
    }

    private void ResetLobbyView()
    {
        CreateLobbyStep = MultiplayerCreateLobbyStep.Setup;
        IsLeaveLobbyDialogOpen = false;
        RoomCode = string.Empty;
        LobbyPlayers.Clear();
    }

    private void ReportFailure(string message)
    {
        LanWorldDiscoveryStatus = message;
        statusService.Report(message);
        floatingMessageService.Show(message);
    }

    private static string MapCreationFailure(MultiplayerLobbyCreationFailure failure)
    {
        return failure switch
        {
            MultiplayerLobbyCreationFailure.TerracottaUnavailable => Strings.Multiplayer_Create_TerracottaUnavailable,
            MultiplayerLobbyCreationFailure.MinecraftWorldUnavailable => Strings.Multiplayer_Create_WorldUnavailable,
            MultiplayerLobbyCreationFailure.TerracottaBusy => Strings.Multiplayer_Create_TerracottaBusy,
            MultiplayerLobbyCreationFailure.TerracottaProtocolFailed => Strings.Multiplayer_Create_TerracottaProtocolFailed,
            _ => Strings.Multiplayer_Create_LobbyFailed
        };
    }

    partial void OnCreateLobbyStepChanged(MultiplayerCreateLobbyStep value)
    {
        if (value is not MultiplayerCreateLobbyStep.Lobby)
            IsLeaveLobbyDialogOpen = false;

        OnPropertyChanged(nameof(IsLobbyStep));
        OnPropertyChanged(nameof(SectionTitle));
        RequestLeaveLobbyCommand.NotifyCanExecuteChanged();
        ConfirmLeaveLobbyCommand.NotifyCanExecuteChanged();
    }

    partial void OnLobbyOwnerNameChanged(string value)
    {
        OnPropertyChanged(nameof(LobbyTitle));
        OnPropertyChanged(nameof(SectionTitle));
    }

    partial void OnRoomCodeChanged(string value)
    {
        CopyRoomCodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCreatingLobbyChanged(bool value)
    {
        OnPropertyChanged(nameof(CreateLobbyButtonText));
    }

    partial void OnLanWorldDiscoveryStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasLanWorldDiscoveryStatus));
    }

    partial void OnSelectedSectionChanged(MultiplayerSectionItem? value)
    {
        foreach (var section in Sections)
            section.IsSelected = ReferenceEquals(section, value);

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(IsCreateLobbySection));
    }

    private static MultiplayerLanWorldItem CreateLanWorldItem(MinecraftLanWorld world)
    {
        var name = string.IsNullOrWhiteSpace(world.Name)
            ? Strings.Multiplayer_Create_UnknownLanWorld
            : world.Name;
        return new MultiplayerLanWorldItem(
            world,
            string.Format(Strings.Multiplayer_Create_LanWorldItemFormat, name, world.Port));
    }

    private void NotifyLanWorldsChanged()
    {
        OnPropertyChanged(nameof(HasLanWorlds));
        OnPropertyChanged(nameof(HasMultipleLanWorlds));
        CreateLobbyCommand.NotifyCanExecuteChanged();
    }
}
