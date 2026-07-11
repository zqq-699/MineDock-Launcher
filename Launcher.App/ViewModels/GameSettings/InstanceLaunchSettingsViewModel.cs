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
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

/// <summary>
/// 编辑实例级启动覆盖项，并在“使用全局设置”和“实例独立设置”之间维持一致的展示与持久化语义。
/// </summary>
public sealed partial class InstanceLaunchSettingsViewModel : GameSettingsDetailsSectionViewModelBase, IDisposable
{
    // 连续拖动内存滑块或快速切换选项时合并保存，避免每个 PropertyChanged 都写一次实例文件。
    private static readonly TimeSpan SaveMergeDelay = TimeSpan.FromMilliseconds(100);
    private readonly ISystemMemoryService systemMemoryService;
    private readonly IModService modService;
    private readonly InstanceSettingsPersistenceCoordinator persistence;
    // Mod 数量参与自动内存估算；切换实例时必须取消旧统计，防止旧实例结果覆盖当前实例。
    private CancellationTokenSource? modCountRefreshCancellation;
    private LauncherSettings globalSettings = new();
    private GameInstance? selectedInstance;
    // 从模型回填编辑器也会触发生成的属性回调。抑制标记用于区分“加载值”和“用户修改”。
    private bool suppressAutoSave;
    private int enabledModCount;

    [ObservableProperty]
    private bool launchCheckFilesBeforeLaunchEnabled;

    [ObservableProperty]
    private bool launchAutoRepairMissingFilesEnabled;

    [ObservableProperty]
    private bool launchMinimizeLauncherAfterLaunchEnabled;

    [ObservableProperty]
    private bool launchFullScreenEnabled;

    [ObservableProperty]
    private string launchPreLaunchCommand = string.Empty;

    [ObservableProperty]
    private bool launchWaitForPreLaunchCommand = true;

    [ObservableProperty]
    private string launchPostExitCommand = string.Empty;

    [ObservableProperty]
    private string launchJvmArguments = string.Empty;

    [ObservableProperty]
    private string launchGameArguments = string.Empty;

    [ObservableProperty]
    private SettingsMemoryModeOption? selectedMemoryModeOption;

    [ObservableProperty]
    private double memoryMb = LauncherDefaults.DefaultMemoryMb;

    [ObservableProperty]
    private int memorySliderMinimumMb = MemoryAllocationCalculator.MinimumMemoryMb;

    [ObservableProperty]
    private int memorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;

    [ObservableProperty]
    private string systemTotalMemoryText = string.Empty;

    [ObservableProperty]
    private string systemAvailableMemoryText = string.Empty;

    [ObservableProperty]
    private int automaticMemoryMb = LauncherDefaults.DefaultMemoryMb;

    [ObservableProperty]
    private GameSettingsLaunchSettingsModeOption? selectedLaunchSettingsModeOption;

    internal InstanceLaunchSettingsViewModel(
        ISystemMemoryService systemMemoryService,
        IModService modService,
        InstanceSettingsPersistenceCoordinator persistence)
    {
        this.systemMemoryService = systemMemoryService;
        this.modService = modService;
        this.persistence = persistence;
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Auto,
            Strings.Settings_MemoryModeAuto));
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Manual,
            Strings.Settings_MemoryModeManual));
        SelectedMemoryModeOption = MemoryModeOptions[0];
        SelectedLaunchSettingsModeOption = LaunchSettingsModeOptions[0];
    }

    public IReadOnlyList<GameSettingsLaunchSettingsModeOption> LaunchSettingsModeOptions { get; } =
    [
        new(LaunchSettingsMode.UseGlobal, Strings.GameSettings_LaunchSettingsModeUseGlobal),
        new(LaunchSettingsMode.PerInstance, Strings.GameSettings_LaunchSettingsModePerInstance)
    ];

    public ObservableCollection<SettingsMemoryModeOption> MemoryModeOptions { get; } = [];

    public bool AreLaunchSettingsOverridesEnabled => SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.PerInstance;

    public bool CanEditAutoRepairMissingFiles => AreLaunchSettingsOverridesEnabled && LaunchCheckFilesBeforeLaunchEnabled;

    public bool IsMemorySliderEnabled => AreLaunchSettingsOverridesEnabled && SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsMemorySliderVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsAutomaticMemorySummaryVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Auto;

    public string MemoryText => MemorySizeTextFormatter.FormatGb(MemoryMb);

    public string AutomaticMemoryText => MemorySizeTextFormatter.FormatGb(AutomaticMemoryMb);

    public string SystemMemorySummaryText => string.Format(
        Strings.Settings_SystemMemorySummaryFormat,
        SystemAvailableMemoryText,
        SystemTotalMemoryText);

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        // 保存同一个设置对象引用，使设置页修改全局默认值后本页无需重新反序列化。
        globalSettings = launcherSettings;
        RefreshSystemMemorySnapshot();
        if (SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();
    }

    public void SetSelectedInstance(GameInstance? instance)
    {
        // 先废弃与旧实例关联的异步工作，再整体回填编辑器，保持 UI 不出现新旧实例混合值。
        selectedInstance = instance;
        CancelModCountRefresh();
        enabledModCount = 0;
        LoadEditorFromInstance();
        RefreshSystemMemorySnapshot();
        _ = RefreshEnabledModCountAsync(instance);
    }

    public override Task OnSectionActivatedAsync()
    {
        RefreshSystemMemorySnapshot();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CancelModCountRefresh();
    }

    public void RefreshSystemMemorySnapshot()
    {
        try
        {
            // 自动内存同时考虑物理内存、Loader 和启用 Mod 数量；滑块上限只由本机资源决定。
            var snapshot = systemMemoryService.GetSnapshot();
            var totalMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.TotalMemoryBytes);
            var availableMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.AvailableMemoryBytes);
            MemorySliderMaximumMb = MemoryAllocationCalculator.CalculateMaximumMemoryMb(totalMemoryMb);
            AutomaticMemoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
                snapshot,
                selectedInstance?.Loader ?? LoaderKind.Vanilla,
                enabledModCount);
            SystemTotalMemoryText = MemorySizeTextFormatter.Format(totalMemoryMb);
            SystemAvailableMemoryText = MemorySizeTextFormatter.FormatGb(availableMemoryMb);
        }
        catch (Exception)
        {
            // 系统指标不可用不应阻断设置页，退回可持久化的保守范围并明确显示“不可用”。
            MemorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;
            AutomaticMemoryMb = NormalizeMemoryValue(MemoryMb);
            SystemTotalMemoryText = Strings.Settings_MemoryUnavailable;
            SystemAvailableMemoryText = Strings.Settings_MemoryUnavailable;
        }
    }

    partial void OnSelectedLaunchSettingsModeOptionChanged(GameSettingsLaunchSettingsModeOption? value)
    {
        OnPropertyChanged(nameof(AreLaunchSettingsOverridesEnabled));
        OnPropertyChanged(nameof(CanEditAutoRepairMissingFiles));
        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        if (suppressAutoSave)
            return;

        // 切回全局模式时立即镜像全局值，避免禁用的控件仍展示过期实例覆盖值。
        if (value?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();
        ScheduleSave();
    }

    partial void OnLaunchCheckFilesBeforeLaunchEnabledChanged(bool value)
    {
        if (!suppressAutoSave)
            ApplyLaunchCheckDependency(value);
        OnPropertyChanged(nameof(CanEditAutoRepairMissingFiles));
        ScheduleSaveUnlessSuppressed();
    }

    partial void OnLaunchAutoRepairMissingFilesEnabledChanged(bool value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchMinimizeLauncherAfterLaunchEnabledChanged(bool value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchFullScreenEnabledChanged(bool value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchPreLaunchCommandChanged(string value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchWaitForPreLaunchCommandChanged(bool value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchPostExitCommandChanged(string value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchJvmArgumentsChanged(string value) => ScheduleSaveUnlessSuppressed();

    partial void OnLaunchGameArgumentsChanged(string value) => ScheduleSaveUnlessSuppressed();

    partial void OnSelectedMemoryModeOptionChanged(SettingsMemoryModeOption? value)
    {
        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        OnPropertyChanged(nameof(IsMemorySliderVisible));
        OnPropertyChanged(nameof(IsAutomaticMemorySummaryVisible));
        ScheduleSaveUnlessSuppressed();
    }

    partial void OnMemoryMbChanged(double value)
    {
        // 程序回填和用户输入都可能越界；先钳制并通过属性自身再次通知，最终只保存规范值。
        var clamped = Math.Clamp(value, MemorySliderMinimumMb, MemorySliderMaximumMb);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            MemoryMb = clamped;
            return;
        }

        OnPropertyChanged(nameof(MemoryText));
        ScheduleSaveUnlessSuppressed();
    }

    partial void OnMemorySliderMaximumMbChanged(int value)
    {
        if (MemoryMb > value)
            MemoryMb = value;
    }

    partial void OnSystemTotalMemoryTextChanged(string value) => OnPropertyChanged(nameof(SystemMemorySummaryText));

    partial void OnSystemAvailableMemoryTextChanged(string value) => OnPropertyChanged(nameof(SystemMemorySummaryText));

    partial void OnAutomaticMemoryMbChanged(int value) => OnPropertyChanged(nameof(AutomaticMemoryText));

    private void ScheduleSaveUnlessSuppressed()
    {
        if (!suppressAutoSave)
            ScheduleSave();
    }

    private void ScheduleSave()
    {
        var instance = selectedInstance;
        if (instance is null)
            return;

        // 在调度前捕获完整快照，延迟执行时不读取可能已经切换到另一实例的 UI 属性。
        var mode = SelectedLaunchSettingsModeOption?.Mode ?? LaunchSettingsMode.UseGlobal;
        var checkFilesBeforeLaunch = LaunchCheckFilesBeforeLaunchEnabled;
        var autoRepairMissingFiles = LaunchAutoRepairMissingFilesEnabled;
        var minimizeLauncherAfterLaunch = LaunchMinimizeLauncherAfterLaunchEnabled;
        var launchFullScreen = LaunchFullScreenEnabled;
        var preLaunchCommand = NormalizeSettingText(LaunchPreLaunchCommand);
        var waitForPreLaunchCommand = LaunchWaitForPreLaunchCommand;
        var postExitCommand = NormalizeSettingText(LaunchPostExitCommand);
        var jvmArguments = NormalizeSettingText(LaunchJvmArguments);
        var gameArguments = NormalizeSettingText(LaunchGameArguments);
        var memorySettingsMode = mode is LaunchSettingsMode.UseGlobal
            ? globalSettings.DefaultMemorySettingsMode
            : SelectedMemoryModeOption?.Mode ?? MemorySettingsMode.Manual;
        var memory = mode is LaunchSettingsMode.UseGlobal
            ? NormalizeMemoryValue(globalSettings.DefaultMemoryMb)
            : NormalizeMemoryValue(MemoryMb);

        // PersistenceCoordinator 按实例和区域合并写入，并在保存失败时调用回填动作恢复 UI。
        persistence.Schedule(
            "launch",
            instance,
            target => ApplyMutation(
                target,
                mode,
                checkFilesBeforeLaunch,
                autoRepairMissingFiles,
                minimizeLauncherAfterLaunch,
                launchFullScreen,
                preLaunchCommand,
                waitForPreLaunchCommand,
                postExitCommand,
                jvmArguments,
                gameArguments,
                memorySettingsMode,
                memory),
            LoadEditorFromInstance,
            SaveMergeDelay);
    }

    private static Action? ApplyMutation(
        GameInstance instance,
        LaunchSettingsMode mode,
        bool checkFilesBeforeLaunch,
        bool autoRepairMissingFiles,
        bool minimizeLauncherAfterLaunch,
        bool launchFullScreen,
        string preLaunchCommand,
        bool waitForPreLaunchCommand,
        string postExitCommand,
        string jvmArguments,
        string gameArguments,
        MemorySettingsMode memorySettingsMode,
        int memoryMb)
    {
        // 先比较规范化快照，未发生语义变化时不制造磁盘写入；发生变化则返回完整回滚闭包。
        var original = InstanceLaunchSettingsSnapshot.Capture(instance);
        if (original.Matches(
                mode,
                checkFilesBeforeLaunch,
                autoRepairMissingFiles,
                minimizeLauncherAfterLaunch,
                launchFullScreen,
                preLaunchCommand,
                waitForPreLaunchCommand,
                postExitCommand,
                jvmArguments,
                gameArguments,
                memorySettingsMode,
                memoryMb))
        {
            return null;
        }

        instance.LaunchSettingsMode = mode;
        instance.CheckFilesBeforeLaunch = checkFilesBeforeLaunch;
        instance.AutoRepairMissingFiles = autoRepairMissingFiles;
        instance.MinimizeLauncherAfterLaunch = minimizeLauncherAfterLaunch;
        instance.LaunchFullScreen = launchFullScreen;
        instance.PreLaunchCommand = preLaunchCommand;
        instance.WaitForPreLaunchCommand = waitForPreLaunchCommand;
        instance.PostExitCommand = postExitCommand;
        instance.JvmArguments = jvmArguments;
        instance.GameArguments = gameArguments;
        instance.MemorySettingsMode = memorySettingsMode;
        instance.MemoryMb = memoryMb;
        return () => original.Restore(instance);
    }

    private void LoadEditorFromInstance()
    {
        // 回填必须包在抑制区间内，否则设置实例会立即把相同值再保存一次。
        suppressAutoSave = true;
        try
        {
            var mode = selectedInstance?.LaunchSettingsMode ?? LaunchSettingsMode.UseGlobal;
            SelectedLaunchSettingsModeOption = ResolveLaunchSettingsModeOption(mode);
            if (mode is LaunchSettingsMode.UseGlobal)
            {
                ApplyGlobalLaunchSettingsToEditorCore();
                return;
            }

            SelectedMemoryModeOption = ResolveMemoryModeOption(selectedInstance?.MemorySettingsMode ?? MemorySettingsMode.Manual);
            MemoryMb = NormalizeMemoryValue(selectedInstance?.MemoryMb ?? LauncherDefaults.DefaultMemoryMb);
            LaunchCheckFilesBeforeLaunchEnabled = selectedInstance?.CheckFilesBeforeLaunch ?? true;
            LaunchAutoRepairMissingFilesEnabled = selectedInstance?.AutoRepairMissingFiles ?? true;
            LaunchMinimizeLauncherAfterLaunchEnabled = selectedInstance?.MinimizeLauncherAfterLaunch ?? false;
            LaunchFullScreenEnabled = selectedInstance?.LaunchFullScreen ?? false;
            LaunchPreLaunchCommand = selectedInstance?.PreLaunchCommand ?? string.Empty;
            LaunchWaitForPreLaunchCommand = selectedInstance?.WaitForPreLaunchCommand ?? true;
            LaunchPostExitCommand = selectedInstance?.PostExitCommand ?? string.Empty;
            LaunchJvmArguments = selectedInstance?.JvmArguments ?? string.Empty;
            LaunchGameArguments = selectedInstance?.GameArguments ?? string.Empty;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private void ApplyGlobalLaunchSettingsToEditor()
    {
        suppressAutoSave = true;
        try
        {
            ApplyGlobalLaunchSettingsToEditorCore();
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private void ApplyGlobalLaunchSettingsToEditorCore()
    {
        LaunchCheckFilesBeforeLaunchEnabled = globalSettings.DefaultCheckFilesBeforeLaunch;
        LaunchAutoRepairMissingFilesEnabled = globalSettings.DefaultAutoRepairMissingFiles;
        LaunchMinimizeLauncherAfterLaunchEnabled = globalSettings.DefaultMinimizeLauncherAfterLaunch;
        LaunchFullScreenEnabled = globalSettings.DefaultLaunchFullScreen;
        LaunchPreLaunchCommand = globalSettings.DefaultPreLaunchCommand;
        LaunchWaitForPreLaunchCommand = globalSettings.DefaultWaitForPreLaunchCommand;
        LaunchPostExitCommand = globalSettings.DefaultPostExitCommand;
        LaunchJvmArguments = globalSettings.DefaultJvmArguments;
        LaunchGameArguments = globalSettings.DefaultGameArguments;
        SelectedMemoryModeOption = ResolveMemoryModeOption(globalSettings.DefaultMemorySettingsMode);
        MemoryMb = NormalizeMemoryValue(globalSettings.DefaultMemoryMb);
    }

    private void ApplyLaunchCheckDependency(bool checkFilesBeforeLaunch)
    {
        if (LaunchAutoRepairMissingFilesEnabled == checkFilesBeforeLaunch)
            return;

        // 自动修复依赖启动前检查。关闭检查时同步关闭修复，重新开启时恢复成可用的默认组合。
        suppressAutoSave = true;
        try
        {
            LaunchAutoRepairMissingFilesEnabled = checkFilesBeforeLaunch;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private async Task RefreshEnabledModCountAsync(GameInstance? instance)
    {
        if (instance is null || instance.Loader is LoaderKind.Vanilla)
        {
            enabledModCount = 0;
            RefreshSystemMemorySnapshot();
            return;
        }

        var cancellation = new CancellationTokenSource();
        modCountRefreshCancellation = cancellation;
        try
        {
            var mods = await modService.GetModsAsync(instance, cancellation.Token);
            // CancellationToken 之外再校验请求身份，覆盖底层服务忽略取消或恰好已完成的竞态。
            if (!ReferenceEquals(modCountRefreshCancellation, cancellation)
                || !string.Equals(selectedInstance?.Id, instance.Id, StringComparison.Ordinal))
            {
                return;
            }

            enabledModCount = mods.Count(mod => mod.IsEnabled);
            RefreshSystemMemorySnapshot();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Mod 统计只是估算增强项；失败时按 0 计算，不让非关键读取破坏整个启动设置页。
            if (string.Equals(selectedInstance?.Id, instance.Id, StringComparison.Ordinal))
            {
                enabledModCount = 0;
                RefreshSystemMemorySnapshot();
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref modCountRefreshCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private void CancelModCountRefresh()
    {
        var cancellation = Interlocked.Exchange(ref modCountRefreshCancellation, null);
        if (cancellation is null)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private SettingsMemoryModeOption ResolveMemoryModeOption(MemorySettingsMode mode)
    {
        return MemoryModeOptions.FirstOrDefault(option => option.Mode == mode) ?? MemoryModeOptions[0];
    }

    private GameSettingsLaunchSettingsModeOption ResolveLaunchSettingsModeOption(LaunchSettingsMode mode)
    {
        return LaunchSettingsModeOptions.FirstOrDefault(option => option.Mode == mode) ?? LaunchSettingsModeOptions[0];
    }

    private int NormalizeMemoryValue(double value)
    {
        return MemoryAllocationCalculator.NormalizeRecordedMemoryMb(value, MemorySliderMaximumMb);
    }

    private static string NormalizeSettingText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    // 快照同时承担无变化检测和失败回滚，字段必须覆盖 ApplyMutation 修改的全部实例属性。
    private sealed record InstanceLaunchSettingsSnapshot(
        LaunchSettingsMode Mode,
        bool CheckFilesBeforeLaunch,
        bool AutoRepairMissingFiles,
        bool MinimizeLauncherAfterLaunch,
        bool LaunchFullScreen,
        string PreLaunchCommand,
        bool WaitForPreLaunchCommand,
        string PostExitCommand,
        string JvmArguments,
        string GameArguments,
        MemorySettingsMode MemorySettingsMode,
        int MemoryMb)
    {
        public static InstanceLaunchSettingsSnapshot Capture(GameInstance instance)
        {
            return new InstanceLaunchSettingsSnapshot(
                instance.LaunchSettingsMode,
                instance.CheckFilesBeforeLaunch,
                instance.AutoRepairMissingFiles,
                instance.MinimizeLauncherAfterLaunch,
                instance.LaunchFullScreen,
                instance.PreLaunchCommand,
                instance.WaitForPreLaunchCommand,
                instance.PostExitCommand,
                instance.JvmArguments,
                instance.GameArguments,
                instance.MemorySettingsMode,
                instance.MemoryMb);
        }

        public bool Matches(
            LaunchSettingsMode mode,
            bool checkFilesBeforeLaunch,
            bool autoRepairMissingFiles,
            bool minimizeLauncherAfterLaunch,
            bool launchFullScreen,
            string preLaunchCommand,
            bool waitForPreLaunchCommand,
            string postExitCommand,
            string jvmArguments,
            string gameArguments,
            MemorySettingsMode memorySettingsMode,
            int memoryMb)
        {
            return Mode == mode
                && CheckFilesBeforeLaunch == checkFilesBeforeLaunch
                && AutoRepairMissingFiles == autoRepairMissingFiles
                && MinimizeLauncherAfterLaunch == minimizeLauncherAfterLaunch
                && LaunchFullScreen == launchFullScreen
                && string.Equals(PreLaunchCommand, preLaunchCommand, StringComparison.Ordinal)
                && WaitForPreLaunchCommand == waitForPreLaunchCommand
                && string.Equals(PostExitCommand, postExitCommand, StringComparison.Ordinal)
                && string.Equals(JvmArguments, jvmArguments, StringComparison.Ordinal)
                && string.Equals(GameArguments, gameArguments, StringComparison.Ordinal)
                && MemorySettingsMode == memorySettingsMode
                && MemoryMb == memoryMb;
        }

        public void Restore(GameInstance instance)
        {
            instance.LaunchSettingsMode = Mode;
            instance.CheckFilesBeforeLaunch = CheckFilesBeforeLaunch;
            instance.AutoRepairMissingFiles = AutoRepairMissingFiles;
            instance.MinimizeLauncherAfterLaunch = MinimizeLauncherAfterLaunch;
            instance.LaunchFullScreen = LaunchFullScreen;
            instance.PreLaunchCommand = PreLaunchCommand;
            instance.WaitForPreLaunchCommand = WaitForPreLaunchCommand;
            instance.PostExitCommand = PostExitCommand;
            instance.JvmArguments = JvmArguments;
            instance.GameArguments = GameArguments;
            instance.MemorySettingsMode = MemorySettingsMode;
            instance.MemoryMb = MemoryMb;
        }
    }
}
