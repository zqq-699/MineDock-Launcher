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
using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class LaunchMemorySettingsViewModel : SettingsSectionViewModelBase
{
    private readonly ISystemMemoryService systemMemoryService;
    private bool synchronizingLaunchCheck;

    internal LaunchMemorySettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        ISystemMemoryService systemMemoryService)
        : base(persistence)
    {
        this.systemMemoryService = systemMemoryService;
        MemoryModeOptions =
        [
            new(MemorySettingsMode.Auto, Strings.Settings_MemoryModeAuto),
            new(MemorySettingsMode.Manual, Strings.Settings_MemoryModeManual)
        ];
        selectedMemoryModeOption = MemoryModeOptions[0];
    }

    public event EventHandler? LaunchDefaultsChanged;

    public ObservableCollection<SettingsMemoryModeOption> MemoryModeOptions { get; }

    [ObservableProperty] private SettingsMemoryModeOption? selectedMemoryModeOption;
    [ObservableProperty] private double defaultMemoryMb = LauncherDefaults.DefaultMemoryMb;
    [ObservableProperty] private int memorySliderMinimumMb = MemoryAllocationCalculator.MinimumMemoryMb;
    [ObservableProperty] private int memorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;
    [ObservableProperty] private string systemTotalMemoryText = string.Empty;
    [ObservableProperty] private string systemAvailableMemoryText = string.Empty;
    [ObservableProperty] private int automaticMemoryMb = LauncherDefaults.DefaultMemoryMb;
    [ObservableProperty] private bool defaultCheckFilesBeforeLaunch = true;
    [ObservableProperty] private bool defaultAutoRepairMissingFiles = true;
    [ObservableProperty] private bool defaultMinimizeLauncherAfterLaunch;
    [ObservableProperty] private bool defaultLaunchFullScreen;
    [ObservableProperty] private string defaultAutoJoinServerAddress = string.Empty;
    [ObservableProperty] private string defaultPreLaunchCommand = string.Empty;
    [ObservableProperty] private bool defaultWaitForPreLaunchCommand = true;
    [ObservableProperty] private string defaultPostExitCommand = string.Empty;
    [ObservableProperty] private string defaultJvmArguments = string.Empty;
    [ObservableProperty] private string defaultGameArguments = string.Empty;

    public bool IsMemorySliderEnabled => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;
    public bool IsMemorySliderVisible => IsMemorySliderEnabled;
    public bool IsAutomaticMemorySummaryVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Auto;
    public string DefaultMemoryText => MemorySizeTextFormatter.FormatGb(DefaultMemoryMb);
    public string AutomaticMemoryText => MemorySizeTextFormatter.FormatGb(AutomaticMemoryMb);
    public string SystemMemorySummaryText => string.Format(
        Strings.Settings_SystemMemorySummaryFormat,
        SystemAvailableMemoryText,
        SystemTotalMemoryText);

    public void Load(LauncherSettings settings)
    {
        RefreshSystemMemorySnapshot();
        LoadState(() =>
        {
            SelectedMemoryModeOption = MemoryModeOptions.FirstOrDefault(option =>
                option.Mode == settings.DefaultMemorySettingsMode) ?? MemoryModeOptions[0];
            DefaultMemoryMb = NormalizeMemoryValue(settings.DefaultMemoryMb);
            DefaultCheckFilesBeforeLaunch = settings.DefaultCheckFilesBeforeLaunch;
            DefaultAutoRepairMissingFiles = settings.DefaultAutoRepairMissingFiles;
            DefaultMinimizeLauncherAfterLaunch = settings.DefaultMinimizeLauncherAfterLaunch;
            DefaultLaunchFullScreen = settings.DefaultLaunchFullScreen;
            DefaultAutoJoinServerAddress = settings.DefaultAutoJoinServerAddress;
            DefaultPreLaunchCommand = settings.DefaultPreLaunchCommand;
            DefaultWaitForPreLaunchCommand = settings.DefaultWaitForPreLaunchCommand;
            DefaultPostExitCommand = settings.DefaultPostExitCommand;
            DefaultJvmArguments = settings.DefaultJvmArguments;
            DefaultGameArguments = settings.DefaultGameArguments;
        });
    }

    public void RefreshSystemMemorySnapshot()
    {
        try
        {
            var snapshot = systemMemoryService.GetSnapshot();
            var totalMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.TotalMemoryBytes);
            var availableMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.AvailableMemoryBytes);
            MemorySliderMaximumMb = MemoryAllocationCalculator.CalculateMaximumMemoryMb(totalMemoryMb);
            AutomaticMemoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(snapshot);
            SystemTotalMemoryText = MemorySizeTextFormatter.Format(totalMemoryMb);
            SystemAvailableMemoryText = MemorySizeTextFormatter.FormatGb(availableMemoryMb);
        }
        catch (Exception)
        {
            MemorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;
            AutomaticMemoryMb = NormalizeMemoryValue(Settings.DefaultMemoryMb);
            SystemTotalMemoryText = Strings.Settings_MemoryUnavailable;
            SystemAvailableMemoryText = Strings.Settings_MemoryUnavailable;
        }
    }

    partial void OnSelectedMemoryModeOptionChanged(
        SettingsMemoryModeOption? oldValue,
        SettingsMemoryModeOption? newValue)
    {
        if (newValue is null)
        {
            LoadState(() => SelectedMemoryModeOption = oldValue ?? MemoryModeOptions[0]);
            return;
        }

        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        OnPropertyChanged(nameof(IsMemorySliderVisible));
        OnPropertyChanged(nameof(IsAutomaticMemorySummaryVisible));
        PersistAndNotify();
    }

    partial void OnDefaultMemoryMbChanged(double value)
    {
        var clamped = Math.Clamp(value, MemorySliderMinimumMb, MemorySliderMaximumMb);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            DefaultMemoryMb = clamped;
            return;
        }
        OnPropertyChanged(nameof(DefaultMemoryText));
        PersistAndNotify();
    }

    partial void OnSystemTotalMemoryTextChanged(string value) => OnPropertyChanged(nameof(SystemMemorySummaryText));
    partial void OnSystemAvailableMemoryTextChanged(string value) => OnPropertyChanged(nameof(SystemMemorySummaryText));
    partial void OnAutomaticMemoryMbChanged(int value) => OnPropertyChanged(nameof(AutomaticMemoryText));

    partial void OnDefaultCheckFilesBeforeLaunchChanged(bool value)
    {
        if (CanPersist && !synchronizingLaunchCheck && DefaultAutoRepairMissingFiles != value)
        {
            synchronizingLaunchCheck = true;
            DefaultAutoRepairMissingFiles = value;
            synchronizingLaunchCheck = false;
        }
        PersistAndNotify();
    }

    partial void OnDefaultAutoRepairMissingFilesChanged(bool value) => PersistAndNotify();
    partial void OnDefaultMinimizeLauncherAfterLaunchChanged(bool value) => PersistAndNotify();
    partial void OnDefaultLaunchFullScreenChanged(bool value) => PersistAndNotify();
    partial void OnDefaultAutoJoinServerAddressChanged(string value) => PersistAndNotify();
    partial void OnDefaultPreLaunchCommandChanged(string value) => PersistAndNotify();
    partial void OnDefaultWaitForPreLaunchCommandChanged(bool value) => PersistAndNotify();
    partial void OnDefaultPostExitCommandChanged(string value) => PersistAndNotify();
    partial void OnDefaultJvmArgumentsChanged(string value) => PersistAndNotify();
    partial void OnDefaultGameArgumentsChanged(string value) => PersistAndNotify();

    private void PersistAndNotify()
    {
        if (!CanPersist || synchronizingLaunchCheck || SelectedMemoryModeOption is null)
            return;

        Persist(settings =>
        {
            settings.DefaultMemorySettingsMode = SelectedMemoryModeOption.Mode;
            settings.DefaultMemoryMb = NormalizeMemoryValue(DefaultMemoryMb);
            settings.DefaultCheckFilesBeforeLaunch = DefaultCheckFilesBeforeLaunch;
            settings.DefaultAutoRepairMissingFiles = DefaultAutoRepairMissingFiles;
            settings.DefaultMinimizeLauncherAfterLaunch = DefaultMinimizeLauncherAfterLaunch;
            settings.DefaultLaunchFullScreen = DefaultLaunchFullScreen;
            settings.DefaultAutoJoinServerAddress = NormalizeText(DefaultAutoJoinServerAddress);
            settings.DefaultPreLaunchCommand = NormalizeText(DefaultPreLaunchCommand);
            settings.DefaultWaitForPreLaunchCommand = DefaultWaitForPreLaunchCommand;
            settings.DefaultPostExitCommand = NormalizeText(DefaultPostExitCommand);
            settings.DefaultJvmArguments = NormalizeText(DefaultJvmArguments);
            settings.DefaultGameArguments = NormalizeText(DefaultGameArguments);
        });
        LaunchDefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private int NormalizeMemoryValue(double memoryMb)
        => MemoryAllocationCalculator.NormalizeRecordedMemoryMb(memoryMb, MemorySliderMaximumMb);

    private static string NormalizeText(string? value) => value?.Trim() ?? string.Empty;
}
