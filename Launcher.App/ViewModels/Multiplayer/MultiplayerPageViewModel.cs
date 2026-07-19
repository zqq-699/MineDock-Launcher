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

namespace Launcher.App.ViewModels.Multiplayer;

public sealed partial class MultiplayerPageViewModel : ObservableObject
{
    private readonly AccountPageViewModel? accountPage;

    [ObservableProperty]
    private MultiplayerSectionItem? selectedSection;

    [ObservableProperty]
    private MultiplayerCreateLobbyStep createLobbyStep;

    [ObservableProperty]
    private string lobbyOwnerName = Strings.Multiplayer_LobbyOwnerPlaceholder;

    [ObservableProperty]
    private bool isLeaveLobbyDialogOpen;

    public MultiplayerPageViewModel()
        : this(null)
    {
    }

    public MultiplayerPageViewModel(AccountPageViewModel? accountPage)
    {
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

    public string SectionTitle => IsLobbyStep
        ? LobbyTitle
        : SelectedSection?.Title ?? Strings.Multiplayer_SectionCreateLobby;

    public bool IsCreateLobbySection => SelectedSection?.Section is MultiplayerPageSection.CreateLobby;

    public bool IsLobbyStep => CreateLobbyStep is MultiplayerCreateLobbyStep.Lobby;

    public string LobbyTitle => string.Format(Strings.Multiplayer_LobbyTitleFormat, LobbyOwnerName);

    [RelayCommand]
    private void SelectSection(MultiplayerSectionItem? section)
    {
        if (section is not null)
            SelectedSection = section;
    }

    [RelayCommand]
    private void CreateLobby()
    {
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

    partial void OnSelectedSectionChanged(MultiplayerSectionItem? value)
    {
        foreach (var section in Sections)
            section.IsSelected = ReferenceEquals(section, value);

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(IsCreateLobbySection));
    }
}
