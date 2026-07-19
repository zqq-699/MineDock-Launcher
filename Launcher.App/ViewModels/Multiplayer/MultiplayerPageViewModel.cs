/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed partial class MultiplayerPageViewModel : ObservableObject
{
    private readonly AccountPageViewModel? accountPage;
    private readonly IMinecraftLanWorldDiscoveryService lanWorldDiscoveryService;

    [ObservableProperty]
    private MultiplayerSectionItem? selectedSection;

    [ObservableProperty]
    private MultiplayerCreateLobbyStep createLobbyStep;

    [ObservableProperty]
    private string lobbyOwnerName = Strings.Multiplayer_LobbyOwnerPlaceholder;

    [ObservableProperty]
    private bool isLeaveLobbyDialogOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateLobbyCommand))]
    private MultiplayerLanWorldItem? selectedLanWorld;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshLanWorldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateLobbyCommand))]
    private bool isRefreshingLanWorlds;

    [ObservableProperty]
    private string lanWorldDiscoveryStatus = Strings.Multiplayer_Create_LanWorldRefreshHint;

    public MultiplayerPageViewModel(
        IMinecraftLanWorldDiscoveryService lanWorldDiscoveryService,
        AccountPageViewModel? accountPage = null)
    {
        this.lanWorldDiscoveryService = lanWorldDiscoveryService;
        this.accountPage = accountPage;
        Sections =
        [
            new(MultiplayerPageSection.CreateLobby, Strings.Multiplayer_SectionCreateLobby, "multiple_player/multi_create"),
            new(MultiplayerPageSection.JoinLobby, Strings.Multiplayer_SectionJoinLobby, "multiple_player/multi_enter")
        ];
        LobbyPlayers =
        [
            new(
                string.Format(Strings.Multiplayer_LobbyPlayerPlaceholderFormat, 1),
                string.Format(Strings.Multiplayer_LobbyClientIdPlaceholderFormat, 1),
                Strings.Multiplayer_LobbyPlayerRoleHost,
                IsHost: true,
                IsFirst: true,
                IsLast: false),
            new(
                string.Format(Strings.Multiplayer_LobbyPlayerPlaceholderFormat, 2),
                string.Format(Strings.Multiplayer_LobbyClientIdPlaceholderFormat, 2),
                Strings.Multiplayer_LobbyPlayerRolePlayer,
                IsHost: false,
                IsFirst: false,
                IsLast: false),
            new(
                string.Format(Strings.Multiplayer_LobbyPlayerPlaceholderFormat, 3),
                string.Format(Strings.Multiplayer_LobbyClientIdPlaceholderFormat, 3),
                Strings.Multiplayer_LobbyPlayerRolePlayer,
                IsHost: false,
                IsFirst: false,
                IsLast: true)
        ];
        SelectedSection = Sections[0];
    }

    public ObservableCollection<MultiplayerSectionItem> Sections { get; }

    public ObservableCollection<MultiplayerLobbyPlayerItem> LobbyPlayers { get; }

    public ObservableCollection<MultiplayerLanWorldItem> LanWorlds { get; } = [];

    public string SectionTitle => IsLobbyStep
        ? LobbyTitle
        : SelectedSection?.Title ?? Strings.Multiplayer_SectionCreateLobby;

    public bool IsCreateLobbySection => SelectedSection?.Section is MultiplayerPageSection.CreateLobby;

    public bool IsLobbyStep => CreateLobbyStep is MultiplayerCreateLobbyStep.Lobby;

    public string LobbyTitle => string.Format(Strings.Multiplayer_LobbyTitleFormat, LobbyOwnerName);

    public bool HasLanWorldDiscoveryStatus => !string.IsNullOrWhiteSpace(LanWorldDiscoveryStatus);

    public bool CanSelectLanWorld => !IsRefreshingLanWorlds;

    private bool CanRefreshLanWorlds => !IsRefreshingLanWorlds;

    private bool CanCreateLobby => SelectedLanWorld is not null && !IsRefreshingLanWorlds;

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
            var previouslySelectedPort = SelectedLanWorld?.World.Port;
            var hasPublishedProgress = false;
            var progress = new Progress<MinecraftLanWorld>(world =>
            {
                if (discoveryCompleted)
                    return;

                if (!hasPublishedProgress)
                {
                    LanWorlds.Clear();
                    SelectedLanWorld = null;
                    hasPublishedProgress = true;
                }

                var item = CreateLanWorldItem(world);
                var existing = LanWorlds.FirstOrDefault(candidate => candidate.World.Port == world.Port);
                if (existing is not null)
                    LanWorlds[LanWorlds.IndexOf(existing)] = item;
                else
                    LanWorlds.Add(item);

                if (previouslySelectedPort == world.Port)
                    SelectedLanWorld = item;
            });
            var discoveredWorlds = await lanWorldDiscoveryService
                .DiscoverLocalWorldsAsync(cancellationToken, progress);
            discoveryCompleted = true;
            var items = discoveredWorlds
                .Select(CreateLanWorldItem)
                .ToArray();

            LanWorlds.Clear();
            foreach (var item in items)
                LanWorlds.Add(item);

            SelectedLanWorld = previouslySelectedPort is null
                ? null
                : items.FirstOrDefault(item => item.World.Port == previouslySelectedPort.Value);
            LanWorldDiscoveryStatus = items.Length == 0
                ? Strings.Multiplayer_Create_LanWorldNotFound
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LanWorldDiscoveryStatus = string.Empty;
        }
        catch (Exception)
        {
            LanWorldDiscoveryStatus = Strings.Multiplayer_Create_LanWorldDiscoveryFailed;
        }
        finally
        {
            discoveryCompleted = true;
            IsRefreshingLanWorlds = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateLobby))]
    private void CreateLobby()
    {
        if (SelectedLanWorld is null)
            return;

        LobbyOwnerName = accountPage?.SelectedAccount?.DisplayName
            ?? Strings.Multiplayer_LobbyOwnerPlaceholder;
        CreateLobbyStep = MultiplayerCreateLobbyStep.Lobby;
    }

    [RelayCommand]
    private void RequestLeaveLobby()
    {
        if (!IsLobbyStep)
            return;

        IsLeaveLobbyDialogOpen = true;
    }

    [RelayCommand]
    private void CancelLeaveLobby()
    {
        IsLeaveLobbyDialogOpen = false;
    }

    [RelayCommand]
    private void ConfirmLeaveLobby()
    {
        IsLeaveLobbyDialogOpen = false;
        CreateLobbyStep = MultiplayerCreateLobbyStep.Setup;
    }

    partial void OnCreateLobbyStepChanged(MultiplayerCreateLobbyStep value)
    {
        if (value is not MultiplayerCreateLobbyStep.Lobby)
            IsLeaveLobbyDialogOpen = false;

        OnPropertyChanged(nameof(IsLobbyStep));
        OnPropertyChanged(nameof(SectionTitle));
    }

    partial void OnLobbyOwnerNameChanged(string value)
    {
        OnPropertyChanged(nameof(LobbyTitle));
        OnPropertyChanged(nameof(SectionTitle));
    }

    partial void OnIsRefreshingLanWorldsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectLanWorld));
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
}
