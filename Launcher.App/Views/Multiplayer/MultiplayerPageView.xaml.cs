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
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;

namespace Launcher.App.Views.Multiplayer;

public partial class MultiplayerPageView : UserControl
{
    private static readonly string[] SectionOrder =
    [
        nameof(MultiplayerPageSection.CreateLobby),
        nameof(MultiplayerPageSection.JoinLobby)
    ];

    private readonly PageTransitionService sectionTransitionService;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private FrameworkElement? sectionContentRoot;

    public MultiplayerPageView()
    {
        InitializeComponent();
        sectionTransitionService = new PageTransitionService(
            Dispatcher,
            _ => sectionContentRoot,
            GetCurrentSectionId(),
            SectionOrder);

        Loaded += MultiplayerPageView_Loaded;
        DataContextChanged += MultiplayerPageView_DataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private void MultiplayerPageView_Loaded(object sender, RoutedEventArgs e)
    {
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void MultiplayerPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= MultiplayerPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += MultiplayerPageViewModel_PropertyChanged;

        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void MultiplayerPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MultiplayerPageViewModel.SelectedSection)
            && sender is MultiplayerPageViewModel viewModel)
        {
            if (sectionContentRoot is null)
                return;

            MultiplayerListFrame.ScrollViewer.ScrollToVerticalOffset(0);
            MultiplayerListFrame.ScrollViewer.UpdateLayout();
            sectionContentRoot.UpdateLayout();
            sectionTransitionService.MoveTo(
                viewModel.SelectedSection?.Section.ToString() ?? SectionOrder[0]);
        }
    }

    private void ResetSectionPresentation()
    {
        if (sectionContentRoot is null)
            return;

        sectionContentRoot.BeginAnimation(OpacityProperty, null);
        sectionContentRoot.Opacity = 1;

        var transform = EnsureTranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (sectionContentRoot?.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        if (sectionContentRoot is not null)
            sectionContentRoot.RenderTransform = transform;
        return transform;
    }

    private string GetCurrentSectionId()
    {
        return (DataContext as MultiplayerPageViewModel)?.SelectedSection?.Section.ToString()
            ?? SectionOrder[0];
    }

    private void SectionContentRoot_OnLoaded(object sender, RoutedEventArgs e)
    {
        sectionContentRoot = sender as FrameworkElement;
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void SectionContentRoot_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sectionContentRoot, sender))
            sectionContentRoot = null;
    }
}
