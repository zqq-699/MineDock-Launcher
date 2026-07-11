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

using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Views.Account.Dialogs;

public partial class AddAccountDialogView : UserControl
{
    public AddAccountDialogView()
    {
        InitializeComponent();
    }

    internal string ThirdPartyPassword => ThirdPartyPasswordBox.Password;

    internal void ClearThirdPartyPassword() => ThirdPartyPasswordBox.Clear();

    private void ThirdPartyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Account.AccountPageViewModel viewModel)
            viewModel.Dialog.ThirdParty.UpdatePasswordState(ThirdPartyPasswordBox.Password.Length > 0);
    }

    private void ThirdPartyCredentialsPanel_OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not false || ThirdPartyPasswordBox is null)
            return;
        if (DataContext is ViewModels.Account.AccountPageViewModel viewModel
            && (viewModel.Dialog.IsThirdPartyProfileSelectionStep
                || viewModel.Dialog.IsThirdPartyImportProgressStep
                || viewModel.Dialog.IsThirdPartyImportResultStep))
        {
            return;
        }
        ThirdPartyPasswordBox.Clear();
    }
}

