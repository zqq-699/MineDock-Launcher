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
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class ControlListSettingsViewModel : SettingsSectionViewModelBase
{
    internal ControlListSettingsViewModel(SettingsPersistenceCoordinator persistence)
        : base(persistence)
    {
        foreach (var control in SettingsInteractiveControlCatalog.Create())
            InteractiveControls.Add(control);
        selectedControlDemoComboOption = InteractiveControls.FirstOrDefault();
        selectedInteractiveControl = InteractiveControls.FirstOrDefault();
    }

    public ObservableCollection<SettingsInteractiveControlItem> InteractiveControls { get; } = [];

    [ObservableProperty] private string controlDemoInputText = Strings.Settings_ControlDemoDefaultInput;
    [ObservableProperty] private string controlDemoMultilineText = Strings.Settings_ControlDemoDefaultMultilineInput;
    [ObservableProperty] private string controlDemoSearchText = string.Empty;
    [ObservableProperty] private string controlDemoStatusText = Strings.Settings_ControlDemoStatusReady;
    [ObservableProperty] private bool controlDemoSwitchEnabled = true;
    [ObservableProperty] private double controlDemoSliderValue = 48;
    [ObservableProperty] private bool controlDemoSecondaryMenuSelected = true;
    [ObservableProperty] private int controlDemoProgress = 64;
    [ObservableProperty] private SettingsInteractiveControlItem? selectedControlDemoComboOption;
    [ObservableProperty] private SettingsInteractiveControlItem? selectedInteractiveControl;

    [RelayCommand]
    private void RunControlDemoAction()
    {
        ControlDemoProgress = ControlDemoProgress >= 100 ? 20 : ControlDemoProgress + 20;
        ControlDemoStatusText = Strings.Settings_ControlDemoStatusClicked;
        ControlDemoSecondaryMenuSelected = !ControlDemoSecondaryMenuSelected;
    }
}
