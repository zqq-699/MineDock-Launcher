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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;

namespace Launcher.App.Views.Multiplayer;

public partial class CreateLobbyView : UserControl
{
    private readonly SlidingContentTransitionCoordinator stepTransition;
    private INotifyPropertyChanged? currentViewModelNotifier;

    public CreateLobbyView()
    {
        InitializeComponent();

        stepTransition = new SlidingContentTransitionCoordinator(
            this,
            CreateLobbyStepHost,
            CreateLobbySetupLayer,
            CreatedLobbyLayer);

        Loaded += CreateLobbyView_Loaded;
        DataContextChanged += CreateLobbyView_DataContextChanged;
    }

    private void CreateLobbyView_Loaded(object sender, RoutedEventArgs e)
    {
        stepTransition.Sync(IsLobbyStep());
    }

    private void CreateLobbyView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= MultiplayerPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += MultiplayerPageViewModel_PropertyChanged;

        stepTransition.Sync(IsLobbyStep());
    }

    private void MultiplayerPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MultiplayerPageViewModel.CreateLobbyStep))
            stepTransition.AnimateTo(IsLobbyStep());
    }

    private bool IsLobbyStep()
    {
        return DataContext is MultiplayerPageViewModel viewModel
            && viewModel.CreateLobbyStep is MultiplayerCreateLobbyStep.Lobby;
    }
}
