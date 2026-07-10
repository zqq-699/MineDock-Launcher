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
using Launcher.App.ViewModels.Account;

namespace Launcher.App.Views.Account;

public partial class AccountPageView : UserControl
{
    private readonly SlidingContentTransitionCoordinator selectionTransition;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private bool? hasSelectedAccountState;

    public AccountPageView()
    {
        InitializeComponent();

        selectionTransition = new SlidingContentTransitionCoordinator(
            this,
            AccountContentHost,
            AccountEmptyStateView,
            AccountDetailsView);

        Loaded += AccountPageView_Loaded;
        DataContextChanged += AccountPageView_DataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private async void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountPageViewModel viewModel)
            await viewModel.Appearance.RefreshCurrentSecondaryContentAsync();
    }

    private void AccountPageView_Loaded(object sender, RoutedEventArgs e)
    {
        hasSelectedAccountState = HasSelectedAccount();
        selectionTransition.Sync(hasSelectedAccountState.Value);
    }

    private void AccountPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= AccountPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += AccountPageViewModel_PropertyChanged;

        hasSelectedAccountState = HasSelectedAccount();
        selectionTransition.Sync(hasSelectedAccountState.Value);
    }

    private void AccountPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountPageViewModel.SelectedAccount))
        {
            var hasSelectedAccount = HasSelectedAccount();
            if (hasSelectedAccountState != hasSelectedAccount)
                selectionTransition.AnimateTo(hasSelectedAccount);

            hasSelectedAccountState = hasSelectedAccount;
        }
    }

    private bool HasSelectedAccount()
    {
        return DataContext is AccountPageViewModel viewModel
            && viewModel.SelectedAccount is not null;
    }
}
