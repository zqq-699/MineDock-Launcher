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
using Launcher.App.Services;
using Launcher.App.Utilities;

namespace Launcher.App.Views.Download;

public partial class DownloadPageView : UserControl
{
    private readonly SlidingContentTransitionCoordinator stepTransition;
    private readonly DownloadVersionListView downloadVersionList;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private INotifyPropertyChanged? currentVersionListNotifier;

    public DownloadPageView()
    {
        InitializeComponent();

        var downloadStepHost = FindDownloadStepHost();
        downloadVersionList = FindStepContent<DownloadVersionListView>(
            downloadStepHost,
            "DownloadVersionList",
            "Download version list view was not found.");

        stepTransition = new SlidingContentTransitionCoordinator(
            this,
            downloadStepHost,
            FindStepContent<FrameworkElement>(downloadStepHost, "VersionListStep", "Version list step was not found."),
            FindStepContent<FrameworkElement>(downloadStepHost, "InstanceOptionsStep", "Instance options step was not found."),
            [FindFloatingButton("InstallStep")]);

        Loaded += DownloadPageView_OnLoaded;
        DataContextChanged += DownloadPageView_OnDataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private void DownloadPageView_OnLoaded(object sender, RoutedEventArgs e)
    {
        stepTransition.Sync(IsInstanceOptionsStep());
    }

    private void DownloadPageView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= DownloadPageViewModel_OnPropertyChanged;
        if (currentVersionListNotifier is not null)
            currentVersionListNotifier.PropertyChanged -= DownloadVersionListViewModel_OnPropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += DownloadPageViewModel_OnPropertyChanged;
        currentVersionListNotifier = (e.NewValue as DownloadPageViewModel)?.VersionList;
        if (currentVersionListNotifier is not null)
            currentVersionListNotifier.PropertyChanged += DownloadVersionListViewModel_OnPropertyChanged;

        stepTransition.Sync(IsInstanceOptionsStep());
    }

    private void DownloadPageViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadPageViewModel.ContentRefreshToken))
        {
            RefreshRightContentView();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.CurrentStep)
            && sender is DownloadPageViewModel viewModel)
        {
            stepTransition.AnimateTo(viewModel.CurrentStep is DownloadPageStep.InstanceOptions);
        }
    }

    private void DownloadVersionListViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadVersionListViewModel.SelectedVersionCategory)
            or nameof(DownloadVersionListViewModel.VersionSearchQuery))
        {
            ResetVersionListScrollPosition();
        }
    }

    private void RefreshRightContentView()
    {
        stepTransition.Sync(IsInstanceOptionsStep());
        ResetVersionListScrollPosition();
    }

    private void ResetVersionListScrollPosition()
    {
        downloadVersionList.ScrollViewer.ScrollToVerticalOffset(0);
        downloadVersionList.RefreshViewport();
    }

    private void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        RefreshRightContentView();
    }

    private bool IsInstanceOptionsStep()
    {
        return DataContext is DownloadPageViewModel viewModel
            && viewModel.CurrentStep is DownloadPageStep.InstanceOptions;
    }

    private FrameworkElement FindDownloadStepHost()
    {
        return DownloadVersionListFrame.ListContent as FrameworkElement
            ?? throw new InvalidOperationException("Download step host content is not available.");
    }

    private static T FindStepContent<T>(DependencyObject root, string tag, string errorMessage)
        where T : FrameworkElement
    {
        return VisualTreeSearch.FindDescendant<T>(root, element => Equals(element.Tag, tag))
            ?? throw new InvalidOperationException(errorMessage);
    }

    private Button FindFloatingButton(string tag)
    {
        if (DownloadVersionListFrame.FloatingContent is not DependencyObject floatingContent)
            throw new InvalidOperationException("Download version floating content is not available.");

        return VisualTreeSearch.FindDescendant<Button>(floatingContent, button => Equals(button.Tag, tag))
            ?? throw new InvalidOperationException($"Download version floating button '{tag}' was not found.");
    }
}


