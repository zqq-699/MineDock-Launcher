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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountSkinModelDialogViewModel : ObservableObject
{
    private string pendingSkinFilePath = string.Empty;
    private bool isChangingExistingSkinModel;

    [ObservableProperty]
    private bool isSkinModelDialogOpen;

    [ObservableProperty]
    private bool isSkinFormatError;

    [ObservableProperty]
    private AccountSkinModelOption? selectedSkinModelOption;

    public ObservableCollection<AccountSkinModelOption> SkinModelOptions { get; } = new(AccountSkinModelOptionFactory.Create());

    public bool CanConfirmSkinModelDialog => IsSkinModelDialogOpen
        && (IsSkinFormatError
            || ((isChangingExistingSkinModel || !string.IsNullOrWhiteSpace(pendingSkinFilePath))
                && SelectedSkinModelOption is not null));

    public bool IsSkinModelSelectionStep => IsSkinModelDialogOpen && !IsSkinFormatError;

    public bool CanShowSkinModelDialogCancelButton => IsSkinModelSelectionStep;

    public string SkinModelDialogTitle => IsSkinFormatError
        ? Strings.Dialog_SkinFormatErrorTitle
        : Strings.Dialog_SkinModelTitle;

    public string SkinModelDialogSubtitle => IsSkinFormatError
        ? Strings.Dialog_SkinFormatErrorSubtitle
        : Strings.Dialog_SkinModelSubtitle;

    public void Open(string skinFilePath)
    {
        pendingSkinFilePath = skinFilePath;
        isChangingExistingSkinModel = false;
        IsSkinFormatError = false;
        SelectedSkinModelOption = null;
        IsSkinModelDialogOpen = true;
        NotifyDialogStateChanged();
    }

    public void OpenForExistingSkin(MinecraftSkinModel skinModel)
    {
        pendingSkinFilePath = string.Empty;
        isChangingExistingSkinModel = true;
        IsSkinFormatError = false;
        SelectedSkinModelOption = SkinModelOptions.FirstOrDefault(option => option.Model == skinModel);
        IsSkinModelDialogOpen = true;
        NotifyDialogStateChanged();
    }

    public void OpenFormatError()
    {
        pendingSkinFilePath = string.Empty;
        isChangingExistingSkinModel = false;
        SelectedSkinModelOption = null;
        IsSkinFormatError = true;
        IsSkinModelDialogOpen = true;
        NotifyDialogStateChanged();
    }

    public void Cancel()
    {
        IsSkinModelDialogOpen = false;
        Reset();
    }

    public void Reset()
    {
        pendingSkinFilePath = string.Empty;
        isChangingExistingSkinModel = false;
        IsSkinFormatError = false;
        SelectedSkinModelOption = null;
        NotifyDialogStateChanged();
    }

    public bool TryConsumeSelection(out string skinFilePath, out MinecraftSkinModel skinModel)
    {
        skinFilePath = string.Empty;
        skinModel = MinecraftSkinModel.Classic;

        if (!CanConfirmSkinModelDialog || SelectedSkinModelOption is null)
            return false;

        skinFilePath = pendingSkinFilePath;
        skinModel = SelectedSkinModelOption.Model;
        IsSkinModelDialogOpen = false;
        Reset();
        return true;
    }

    partial void OnIsSkinModelDialogOpenChanged(bool value)
    {
        NotifyDialogStateChanged();
    }

    partial void OnIsSkinFormatErrorChanged(bool value)
    {
        NotifyDialogStateChanged();
    }

    partial void OnSelectedSkinModelOptionChanged(AccountSkinModelOption? value)
    {
        OnPropertyChanged(nameof(CanConfirmSkinModelDialog));
    }

    private void NotifyDialogStateChanged()
    {
        OnPropertyChanged(nameof(CanConfirmSkinModelDialog));
        OnPropertyChanged(nameof(IsSkinModelSelectionStep));
        OnPropertyChanged(nameof(CanShowSkinModelDialogCancelButton));
        OnPropertyChanged(nameof(SkinModelDialogTitle));
        OnPropertyChanged(nameof(SkinModelDialogSubtitle));
    }
}

