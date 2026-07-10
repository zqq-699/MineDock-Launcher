/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.Services;
using Launcher.App.Utilities;

namespace Launcher.App.Views.GameSettings;

public partial class GameSettingsPageView : UserControl
{
    private readonly SlidingContentTransitionCoordinator stepTransition;
    private SlidingContentTransitionCoordinator? secondaryMenuTransition;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private readonly DispatcherTimer memoryRefreshTimer;
    private bool isWaitingForSecondaryMenuTransition;

    public GameSettingsPageView()
    {
        InitializeComponent();

        var stepHost = FindStepHost();
        stepTransition = new SlidingContentTransitionCoordinator(
            this,
            stepHost,
            FindStepContent<FrameworkElement>(stepHost, "InstanceListStep", "Instance list step was not found."),
            FindStepContent<FrameworkElement>(stepHost, "InstanceDetailsStep", "Instance details step was not found."));

        Loaded += GameSettingsPageView_Loaded;
        Unloaded += GameSettingsPageView_Unloaded;
        DataContextChanged += GameSettingsPageView_DataContextChanged;
        memoryRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        memoryRefreshTimer.Tick += MemoryRefreshTimer_Tick;
    }

    public FrameworkElement RootElement => PageRoot;

    private void GameSettingsPageView_Loaded(object sender, RoutedEventArgs e)
    {
        stepTransition.Sync(IsDetailsStep());
        EnsureSecondaryMenuTransition();
        secondaryMenuTransition?.Sync(IsDetailsStep());
        RefreshMemorySnapshot();
        memoryRefreshTimer.Start();
    }

    private void GameSettingsPageView_Unloaded(object sender, RoutedEventArgs e)
    {
        memoryRefreshTimer.Stop();
    }

    private void MemoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMemorySnapshot();
    }

    private void RefreshMemorySnapshot()
    {
        if (DataContext is GameSettingsPageViewModel viewModel)
            viewModel.Details.Launch.RefreshSystemMemorySnapshot();
    }

    private void GameSettingsPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= GameSettingsPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += GameSettingsPageViewModel_PropertyChanged;

        stepTransition.Sync(IsDetailsStep());
        EnsureSecondaryMenuTransition();
        secondaryMenuTransition?.Sync(IsDetailsStep());
    }

    private void GameSettingsPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsPageViewModel.CurrentStep)
            && sender is GameSettingsPageViewModel viewModel)
        {
            EnsureSecondaryMenuTransition();
            stepTransition.AnimateTo(viewModel.CurrentStep is GameSettingsPageStep.Details);
            secondaryMenuTransition?.AnimateTo(viewModel.CurrentStep is GameSettingsPageStep.Details);
        }
    }

    private void EnsureSecondaryMenuTransition()
    {
        if (secondaryMenuTransition is not null)
        {
            if (isWaitingForSecondaryMenuTransition)
            {
                LayoutUpdated -= GameSettingsPageView_LayoutUpdated;
                isWaitingForSecondaryMenuTransition = false;
            }
            return;
        }

        var secondaryMenuHost = TryFindStepContent<FrameworkElement>(this, "SecondaryMenuStepHost");
        if (secondaryMenuHost is null)
        {
            WaitForSecondaryMenuTransition();
            return;
        }

        var instanceCategoryMenuLayer = TryFindStepContent<FrameworkElement>(secondaryMenuHost, "InstanceCategoryMenuLayer");
        var detailsSectionMenuLayer = TryFindStepContent<FrameworkElement>(secondaryMenuHost, "DetailsSectionMenuLayer");
        if (instanceCategoryMenuLayer is null || detailsSectionMenuLayer is null)
        {
            WaitForSecondaryMenuTransition();
            return;
        }

        secondaryMenuTransition = new SlidingContentTransitionCoordinator(
            this,
            secondaryMenuHost,
            instanceCategoryMenuLayer,
            detailsSectionMenuLayer,
            useSlideTransition: false,
            useScaleTransition: true,
            transitionScale: 0.96);
        secondaryMenuTransition.Sync(IsDetailsStep());

        if (isWaitingForSecondaryMenuTransition)
        {
            LayoutUpdated -= GameSettingsPageView_LayoutUpdated;
            isWaitingForSecondaryMenuTransition = false;
        }
    }

    private void WaitForSecondaryMenuTransition()
    {
        if (isWaitingForSecondaryMenuTransition)
            return;

        LayoutUpdated += GameSettingsPageView_LayoutUpdated;
        isWaitingForSecondaryMenuTransition = true;
    }

    private void GameSettingsPageView_LayoutUpdated(object? sender, EventArgs e)
    {
        EnsureSecondaryMenuTransition();
    }

    private bool IsDetailsStep()
    {
        return DataContext is GameSettingsPageViewModel viewModel
            && viewModel.CurrentStep is GameSettingsPageStep.Details;
    }

    private FrameworkElement FindStepHost()
    {
        return GameSettingsListFrame.ListContent as FrameworkElement
            ?? throw new InvalidOperationException("Game settings step host content is not available.");
    }

    private static T FindStepContent<T>(DependencyObject root, string tag, string errorMessage)
        where T : FrameworkElement
    {
        return TryFindStepContent<T>(root, tag)
            ?? throw new InvalidOperationException(errorMessage);
    }

    private static T? TryFindStepContent<T>(DependencyObject root, string tag)
        where T : FrameworkElement
    {
        return VisualTreeSearch.FindDescendant<T>(root, element => Equals(element.Tag, tag));
    }

}
