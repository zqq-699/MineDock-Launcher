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

using System.Collections;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Controls;

/// <summary>
/// 为启动设置编辑器提供高级参数文本框的内容高度测量，避免固定高度截断多行参数。
/// </summary>
public partial class LaunchSettingsEditor : UserControl
{
    // 这里只处理依赖模板测量的 UI 行为，设置值和保存仍由 ViewModel/Binding 管理。
    public static readonly DependencyProperty ShowModeSelectorProperty =
        DependencyProperty.Register(nameof(ShowModeSelector), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(true));

    public static readonly DependencyProperty LaunchSettingsModeOptionsProperty =
        DependencyProperty.Register(nameof(LaunchSettingsModeOptions), typeof(IEnumerable), typeof(LaunchSettingsEditor), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedLaunchSettingsModeOptionProperty =
        DependencyProperty.Register(nameof(SelectedLaunchSettingsModeOption), typeof(object), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AreLaunchSettingsOverridesEnabledProperty =
        DependencyProperty.Register(nameof(AreLaunchSettingsOverridesEnabled), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(true));

    public static readonly DependencyProperty ShowMemorySettingsProperty =
        DependencyProperty.Register(nameof(ShowMemorySettings), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty MemorySettingsModeOptionsProperty =
        DependencyProperty.Register(nameof(MemorySettingsModeOptions), typeof(IEnumerable), typeof(LaunchSettingsEditor), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedMemorySettingsModeOptionProperty =
        DependencyProperty.Register(nameof(SelectedMemorySettingsModeOption), typeof(object), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MemoryMbProperty =
        DependencyProperty.Register(nameof(MemoryMb), typeof(double), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(4096d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MemoryMinimumMbProperty =
        DependencyProperty.Register(nameof(MemoryMinimumMb), typeof(double), typeof(LaunchSettingsEditor), new PropertyMetadata(1024d));

    public static readonly DependencyProperty MemoryMaximumMbProperty =
        DependencyProperty.Register(nameof(MemoryMaximumMb), typeof(double), typeof(LaunchSettingsEditor), new PropertyMetadata(32768d));

    public static readonly DependencyProperty MemoryStepMbProperty =
        DependencyProperty.Register(nameof(MemoryStepMb), typeof(double), typeof(LaunchSettingsEditor), new PropertyMetadata(512d));

    public static readonly DependencyProperty IsMemorySliderEnabledProperty =
        DependencyProperty.Register(nameof(IsMemorySliderEnabled), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMemorySliderVisibleProperty =
        DependencyProperty.Register(nameof(IsMemorySliderVisible), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty IsAutomaticMemorySummaryVisibleProperty =
        DependencyProperty.Register(nameof(IsAutomaticMemorySummaryVisible), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty AutomaticMemoryTextProperty =
        DependencyProperty.Register(nameof(AutomaticMemoryText), typeof(string), typeof(LaunchSettingsEditor), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MemoryValueTextProperty =
        DependencyProperty.Register(nameof(MemoryValueText), typeof(string), typeof(LaunchSettingsEditor), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SystemTotalMemoryTextProperty =
        DependencyProperty.Register(nameof(SystemTotalMemoryText), typeof(string), typeof(LaunchSettingsEditor), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SystemAvailableMemoryTextProperty =
        DependencyProperty.Register(nameof(SystemAvailableMemoryText), typeof(string), typeof(LaunchSettingsEditor), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SystemMemorySummaryTextProperty =
        DependencyProperty.Register(nameof(SystemMemorySummaryText), typeof(string), typeof(LaunchSettingsEditor), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CanEditAutoRepairMissingFilesProperty =
        DependencyProperty.Register(nameof(CanEditAutoRepairMissingFiles), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(true));

    public static readonly DependencyProperty LaunchCheckFilesBeforeLaunchEnabledProperty =
        DependencyProperty.Register(nameof(LaunchCheckFilesBeforeLaunchEnabled), typeof(bool), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchAutoRepairMissingFilesEnabledProperty =
        DependencyProperty.Register(nameof(LaunchAutoRepairMissingFilesEnabled), typeof(bool), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchMinimizeLauncherAfterLaunchEnabledProperty =
        DependencyProperty.Register(nameof(LaunchMinimizeLauncherAfterLaunchEnabled), typeof(bool), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchFullScreenEnabledProperty =
        DependencyProperty.Register(nameof(LaunchFullScreenEnabled), typeof(bool), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchPreLaunchCommandProperty =
        DependencyProperty.Register(nameof(LaunchPreLaunchCommand), typeof(string), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchWaitForPreLaunchCommandProperty =
        DependencyProperty.Register(nameof(LaunchWaitForPreLaunchCommand), typeof(bool), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchPostExitCommandProperty =
        DependencyProperty.Register(nameof(LaunchPostExitCommand), typeof(string), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchJvmArgumentsProperty =
        DependencyProperty.Register(nameof(LaunchJvmArguments), typeof(string), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LaunchGameArgumentsProperty =
        DependencyProperty.Register(nameof(LaunchGameArguments), typeof(string), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public LaunchSettingsEditor()
    {
        InitializeComponent();
    }

    private void AdvancedLaunchTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        QueueAdvancedLaunchTextBoxHeightUpdate(sender as TextBox);
    }

    private void AdvancedLaunchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        QueueAdvancedLaunchTextBoxHeightUpdate(sender as TextBox);
    }

    private void AdvancedLaunchTextBox_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            QueueAdvancedLaunchTextBoxHeightUpdate(sender as TextBox);
    }

    private void QueueAdvancedLaunchTextBoxHeightUpdate(TextBox? textBox)
    {
        // TextChanged 和 SizeChanged 会连续触发，延迟到布局队列后使用最终宽度计算一次。
        if (textBox is null)
            return;

        Dispatcher.BeginInvoke(() => UpdateAdvancedLaunchTextBoxHeight(textBox), DispatcherPriority.Background);
    }

    private static void UpdateAdvancedLaunchTextBoxHeight(TextBox textBox)
    {
        // 按实际换行内容测量并钳制到资源范围，既避免截断也保留页面滚动能力。
        textBox.UpdateLayout();
        var lineCount = Math.Max(1, textBox.LineCount);
        textBox.VerticalContentAlignment = lineCount > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;
        var lineHeight = TextBlock.GetLineHeight(textBox);
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(
                textBox.FontFamily.LineSpacing * textBox.FontSize,
                textBox.FontSize * 1.35);
        }
        var chromeAllowance = 16d;
        textBox.Height = Math.Max(34, Math.Ceiling((lineCount * lineHeight) + chromeAllowance));
    }

    public bool ShowModeSelector
    {
        get => (bool)GetValue(ShowModeSelectorProperty);
        set => SetValue(ShowModeSelectorProperty, value);
    }

    public IEnumerable? LaunchSettingsModeOptions
    {
        get => (IEnumerable?)GetValue(LaunchSettingsModeOptionsProperty);
        set => SetValue(LaunchSettingsModeOptionsProperty, value);
    }

    public object? SelectedLaunchSettingsModeOption
    {
        get => GetValue(SelectedLaunchSettingsModeOptionProperty);
        set => SetValue(SelectedLaunchSettingsModeOptionProperty, value);
    }

    public bool AreLaunchSettingsOverridesEnabled
    {
        get => (bool)GetValue(AreLaunchSettingsOverridesEnabledProperty);
        set => SetValue(AreLaunchSettingsOverridesEnabledProperty, value);
    }

    public bool ShowMemorySettings
    {
        get => (bool)GetValue(ShowMemorySettingsProperty);
        set => SetValue(ShowMemorySettingsProperty, value);
    }

    public IEnumerable? MemorySettingsModeOptions
    {
        get => (IEnumerable?)GetValue(MemorySettingsModeOptionsProperty);
        set => SetValue(MemorySettingsModeOptionsProperty, value);
    }

    public object? SelectedMemorySettingsModeOption
    {
        get => GetValue(SelectedMemorySettingsModeOptionProperty);
        set => SetValue(SelectedMemorySettingsModeOptionProperty, value);
    }

    public double MemoryMb
    {
        get => (double)GetValue(MemoryMbProperty);
        set => SetValue(MemoryMbProperty, value);
    }

    public double MemoryMinimumMb
    {
        get => (double)GetValue(MemoryMinimumMbProperty);
        set => SetValue(MemoryMinimumMbProperty, value);
    }

    public double MemoryMaximumMb
    {
        get => (double)GetValue(MemoryMaximumMbProperty);
        set => SetValue(MemoryMaximumMbProperty, value);
    }

    public double MemoryStepMb
    {
        get => (double)GetValue(MemoryStepMbProperty);
        set => SetValue(MemoryStepMbProperty, value);
    }

    public bool IsMemorySliderEnabled
    {
        get => (bool)GetValue(IsMemorySliderEnabledProperty);
        set => SetValue(IsMemorySliderEnabledProperty, value);
    }

    public bool IsMemorySliderVisible
    {
        get => (bool)GetValue(IsMemorySliderVisibleProperty);
        set => SetValue(IsMemorySliderVisibleProperty, value);
    }

    public bool IsAutomaticMemorySummaryVisible
    {
        get => (bool)GetValue(IsAutomaticMemorySummaryVisibleProperty);
        set => SetValue(IsAutomaticMemorySummaryVisibleProperty, value);
    }

    public string AutomaticMemoryText
    {
        get => (string)GetValue(AutomaticMemoryTextProperty);
        set => SetValue(AutomaticMemoryTextProperty, value);
    }

    public string MemoryValueText
    {
        get => (string)GetValue(MemoryValueTextProperty);
        set => SetValue(MemoryValueTextProperty, value);
    }

    public string SystemTotalMemoryText
    {
        get => (string)GetValue(SystemTotalMemoryTextProperty);
        set => SetValue(SystemTotalMemoryTextProperty, value);
    }

    public string SystemAvailableMemoryText
    {
        get => (string)GetValue(SystemAvailableMemoryTextProperty);
        set => SetValue(SystemAvailableMemoryTextProperty, value);
    }

    public string SystemMemorySummaryText
    {
        get => (string)GetValue(SystemMemorySummaryTextProperty);
        set => SetValue(SystemMemorySummaryTextProperty, value);
    }

    public bool CanEditAutoRepairMissingFiles
    {
        get => (bool)GetValue(CanEditAutoRepairMissingFilesProperty);
        set => SetValue(CanEditAutoRepairMissingFilesProperty, value);
    }

    public bool LaunchCheckFilesBeforeLaunchEnabled
    {
        get => (bool)GetValue(LaunchCheckFilesBeforeLaunchEnabledProperty);
        set => SetValue(LaunchCheckFilesBeforeLaunchEnabledProperty, value);
    }

    public bool LaunchAutoRepairMissingFilesEnabled
    {
        get => (bool)GetValue(LaunchAutoRepairMissingFilesEnabledProperty);
        set => SetValue(LaunchAutoRepairMissingFilesEnabledProperty, value);
    }

    public bool LaunchMinimizeLauncherAfterLaunchEnabled
    {
        get => (bool)GetValue(LaunchMinimizeLauncherAfterLaunchEnabledProperty);
        set => SetValue(LaunchMinimizeLauncherAfterLaunchEnabledProperty, value);
    }

    public bool LaunchFullScreenEnabled
    {
        get => (bool)GetValue(LaunchFullScreenEnabledProperty);
        set => SetValue(LaunchFullScreenEnabledProperty, value);
    }

    public string LaunchPreLaunchCommand
    {
        get => (string)GetValue(LaunchPreLaunchCommandProperty);
        set => SetValue(LaunchPreLaunchCommandProperty, value);
    }

    public bool LaunchWaitForPreLaunchCommand
    {
        get => (bool)GetValue(LaunchWaitForPreLaunchCommandProperty);
        set => SetValue(LaunchWaitForPreLaunchCommandProperty, value);
    }

    public string LaunchPostExitCommand
    {
        get => (string)GetValue(LaunchPostExitCommandProperty);
        set => SetValue(LaunchPostExitCommandProperty, value);
    }

    public string LaunchJvmArguments
    {
        get => (string)GetValue(LaunchJvmArgumentsProperty);
        set => SetValue(LaunchJvmArgumentsProperty, value);
    }

    public string LaunchGameArguments
    {
        get => (string)GetValue(LaunchGameArgumentsProperty);
        set => SetValue(LaunchGameArgumentsProperty, value);
    }
}
