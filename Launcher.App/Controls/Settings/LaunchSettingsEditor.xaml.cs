using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Launcher.App.Controls;

public partial class LaunchSettingsEditor : UserControl
{
    public static readonly DependencyProperty ShowModeSelectorProperty =
        DependencyProperty.Register(nameof(ShowModeSelector), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(true));

    public static readonly DependencyProperty LaunchSettingsModeOptionsProperty =
        DependencyProperty.Register(nameof(LaunchSettingsModeOptions), typeof(IEnumerable), typeof(LaunchSettingsEditor), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedLaunchSettingsModeOptionProperty =
        DependencyProperty.Register(nameof(SelectedLaunchSettingsModeOption), typeof(object), typeof(LaunchSettingsEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AreLaunchSettingsOverridesEnabledProperty =
        DependencyProperty.Register(nameof(AreLaunchSettingsOverridesEnabled), typeof(bool), typeof(LaunchSettingsEditor), new PropertyMetadata(true));

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
