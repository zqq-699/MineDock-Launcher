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
using System.Windows.Media;
using Launcher.App.Effects;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Launcher.App.Services;

/// <summary>
/// 在 UI 线程上原子替换主题与强调色资源字典，并跟踪系统主题和渐进模糊能力。
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    internal const string SurfaceBackdropBlurEnabledResourceKey = "Is.Surface.BackdropBlur.Enabled";

    // ResourceDictionary 顺序决定覆盖优先级；主题和 Accent 必须分别替换而不能全量清空应用资源。
    private const string DarkThemeSource =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/Themes/Dark.xaml";

    private const string LightThemeSource =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/Themes/Light.xaml";

    private const string AccentThemeSourcePrefix =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/Themes/Accents/";

    private const string ImageBackgroundStyleSource =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/Themes/Backgrounds/Image.xaml";

    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<ThemeService> logger;
    private readonly IProgressiveBlurSupport progressiveBlurSupport;
    private string preferredTheme = LauncherDefaults.DefaultTheme;
    private string preferredAccentColor = LauncherDefaults.DefaultAccentColor;
    private bool followSystem = true;
    private int backgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;
    private bool disableBackgroundBlur;
    private bool imageBackgroundStylesEnabled;
    private bool hasAppliedTheme;
    private bool isDisposed;
    private ProgressiveBlurCapabilitySnapshot? lastLoggedProgressiveBlurCapability;
    private bool? lastLoggedProgressiveBlurState;

    public ThemeService(IUiDispatcher uiDispatcher, ILogger<ThemeService>? logger = null)
        : this(uiDispatcher, logger, new WpfProgressiveBlurSupport())
    {
    }

    internal ThemeService(
        IUiDispatcher uiDispatcher,
        ILogger<ThemeService>? logger,
        IProgressiveBlurSupport progressiveBlurSupport)
    {
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<ThemeService>.Instance;
        this.progressiveBlurSupport = progressiveBlurSupport;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        this.progressiveBlurSupport.AvailabilityChanged += ProgressiveBlurSupport_AvailabilityChanged;
    }

    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public bool BackgroundBlurDisabled => disableBackgroundBlur;

    public bool ImageBackgroundStylesEnabled => imageBackgroundStylesEnabled;

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public event EventHandler<BackgroundBlurDisabledChangedEventArgs>? BackgroundBlurDisabledChanged;

    public void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent,
        bool disableBackgroundBlur)
    {
        // 保存用户偏好与计算后的有效主题分开，跟随系统时系统事件只需重新计算有效值。
        var backgroundBlurDisabledChanged = this.disableBackgroundBlur != disableBackgroundBlur;
        preferredTheme = NormalizeTheme(theme);
        this.followSystem = followSystem;
        this.backgroundOpacityPercent = NormalizeBackgroundOpacity(backgroundOpacityPercent);
        this.disableBackgroundBlur = disableBackgroundBlur;
        var nextTheme = ResolveEffectiveTheme(preferredTheme, followSystem);
        uiDispatcher.Invoke(() => ApplyEffectiveTheme(nextTheme));
        if (backgroundBlurDisabledChanged)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(this.disableBackgroundBlur));
    }

    public void ApplyAccent(string? accentColor)
    {
        // Accent 名称先规范化到已知资源文件，未知值回退默认色避免资源查找失败。
        var normalizedAccentColor = LauncherAccentColors.Normalize(accentColor);
        if (!string.IsNullOrWhiteSpace(accentColor)
            && !string.Equals(accentColor, normalizedAccentColor, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Invalid launcher accent color preference encountered. AccentColor={AccentColor} FallingBackTo={FallbackAccentColor}",
                accentColor,
                normalizedAccentColor);
        }

        preferredAccentColor = normalizedAccentColor;
        uiDispatcher.Invoke(() =>
        {
            ApplyAccentCore(preferredAccentColor);
            ApplyImageBackgroundStylesCore();
        });
    }

    public void ApplyBackgroundOpacity(int opacityPercent)
    {
        backgroundOpacityPercent = NormalizeBackgroundOpacity(opacityPercent);
        uiDispatcher.Invoke(() => ApplyBackgroundOpacityCore(
            ResolveEffectiveBackgroundOpacityPercent(backgroundOpacityPercent, disableBackgroundBlur)));
    }

    public void ApplyBackgroundBlurDisabled(bool disabled)
    {
        var changed = disableBackgroundBlur != disabled;
        disableBackgroundBlur = disabled;
        uiDispatcher.Invoke(() => ApplyBackgroundOpacityCore(
            ResolveEffectiveBackgroundOpacityPercent(backgroundOpacityPercent, disableBackgroundBlur)));
        if (changed)
            BackgroundBlurDisabledChanged?.Invoke(this, new BackgroundBlurDisabledChangedEventArgs(disableBackgroundBlur));
    }

    public void ApplyImageBackgroundStyles(bool enabled)
    {
        imageBackgroundStylesEnabled = enabled;
        uiDispatcher.Invoke(() => ApplyImageBackgroundStylesCore());
    }

    public object? GetResource(object key)
    {
        object? resource = null;
        uiDispatcher.Invoke(() =>
        {
            resource = global::System.Windows.Application.Current?.TryFindResource(key);
        });
        return resource;
    }

    public Brush? GetBrush(object key)
    {
        return GetResource(key) as Brush;
    }

    public Color? GetColor(object key)
    {
        return GetResource(key) as Color?;
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        progressiveBlurSupport.AvailabilityChanged -= ProgressiveBlurSupport_AvailabilityChanged;
        progressiveBlurSupport.Dispose();
        isDisposed = true;
    }

    private void ApplyEffectiveTheme(EffectiveTheme nextTheme)
    {
        // 在 UI 线程一次替换目标字典，DynamicResource 会随后统一刷新，避免短暂缺少颜色资源。
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsThemeDictionary(dictionaries[index]))
                dictionaries.RemoveAt(index);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(GetThemeSource(nextTheme), UriKind.Absolute)
        });
        ApplyBackgroundOpacityCore(
            ResolveEffectiveBackgroundOpacityPercent(backgroundOpacityPercent, disableBackgroundBlur));
        ApplyProgressiveBlurAvailabilityCore();
        ApplyAccentCore(preferredAccentColor);
        ApplyImageBackgroundStylesCore(nextTheme);

        var oldTheme = EffectiveTheme;
        EffectiveTheme = nextTheme;
        if (!hasAppliedTheme)
        {
            hasAppliedTheme = true;
            return;
        }

        if (oldTheme != nextTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, nextTheme));
    }

    private EffectiveTheme ResolveEffectiveTheme(string theme, bool useSystemTheme)
    {
        if (useSystemTheme)
            return ResolveSystemTheme();

        return string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? EffectiveTheme.Light
            : EffectiveTheme.Dark;
    }

    private static EffectiveTheme ResolveSystemTheme()
    {
        // 系统主题读取失败时采用稳定默认值，注册表故障不能阻止应用启动。
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int appsUseLightTheme && appsUseLightTheme > 0
                ? EffectiveTheme.Light
                : EffectiveTheme.Dark;
        }
        catch
        {
            return EffectiveTheme.Dark;
        }
    }

    private static string NormalizeTheme(string? theme)
    {
        return string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : LauncherDefaults.DefaultTheme;
    }

    private static int NormalizeBackgroundOpacity(int opacityPercent)
    {
        return Math.Clamp(opacityPercent, 0, 100);
    }

    internal static int ResolveEffectiveBackgroundOpacityPercent(
        int preferredOpacityPercent,
        bool backgroundBlurDisabled)
    {
        return backgroundBlurDisabled
            ? 100
            : NormalizeBackgroundOpacity(preferredOpacityPercent);
    }

    private static void ApplyBackgroundOpacityCore(int opacityPercent)
    {
        // 透明度通过共享动态资源传播，页面和控件不各自计算 Brush alpha。
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        application.Resources["Opacity.Page.Background"] = NormalizeBackgroundOpacity(opacityPercent) / 100d;
    }

    private void ApplyProgressiveBlurAvailabilityCore()
    {
        // 应用内列表渐进模糊只由运行时 Shader 能力决定，与窗口背景效果偏好相互独立。
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var capability = progressiveBlurSupport.Current;
        var progressiveBlurActive = capability.IsAvailable;
        application.Resources[ProgressiveBlurResourceKeys.IsEnabled] = progressiveBlurActive;

        LogProgressiveBlurCapability(capability);
        if (lastLoggedProgressiveBlurState != progressiveBlurActive)
        {
            logger.LogDebug(
                "Progressive blur availability applied. ProgressiveBlurActive={ProgressiveBlurActive}",
                progressiveBlurActive);
            lastLoggedProgressiveBlurState = progressiveBlurActive;
        }
    }

    private void LogProgressiveBlurCapability(ProgressiveBlurCapabilitySnapshot capability)
    {
        // 只在有效状态变化时记录，避免每次资源刷新重复输出相同能力日志。
        if (lastLoggedProgressiveBlurCapability == capability)
            return;

        if (capability.UnavailableReason is ProgressiveBlurUnavailableReason.ShaderLoadFailed
            && capability.InitializationException is not null)
        {
            logger.LogWarning(
                capability.InitializationException,
                "Progressive blur shader initialization failed; opacity fade fallback will be used. RenderTier={RenderTier} ShaderModel=3.0 HardwareOnly=True",
                capability.RenderingTier);
        }
        else if (capability.UnavailableReason is ProgressiveBlurUnavailableReason.ShaderRejected)
        {
            logger.LogWarning(
                "Progressive blur shader was rejected by WPF; opacity fade fallback will be used. RenderTier={RenderTier} ShaderModel=3.0 HardwareOnly=True",
                capability.RenderingTier);
        }
        else
        {
            logger.LogDebug(
                "Progressive blur capability evaluated. Supported={Supported} RenderTier={RenderTier} PixelShader30Supported={PixelShader30Supported} ShaderModel=3.0 Reason={Reason} HardwareOnly=True",
                capability.IsAvailable,
                capability.RenderingTier,
                capability.IsPixelShader30Supported,
                capability.UnavailableReason);
        }

        lastLoggedProgressiveBlurCapability = capability;
    }

    private void ProgressiveBlurSupport_AvailabilityChanged(object? sender, EventArgs e)
    {
        if (isDisposed)
            return;

        uiDispatcher.Invoke(ApplyProgressiveBlurAvailabilityCore);
    }

    private void ApplyAccentCore(string accentColor)
    {
        // 只移除现有 Accent 字典，保留主题、共享样式和第三方资源顺序。
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsAccentDictionary(dictionaries[index]))
                dictionaries.RemoveAt(index);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(GetAccentThemeSource(accentColor), UriKind.Absolute)
        });
        logger.LogDebug("Launcher accent applied. AccentColor={AccentColor}", accentColor);
    }

    private void ApplyImageBackgroundStylesCore(EffectiveTheme? effectiveThemeOverride = null)
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsImageBackgroundStyleDictionary(dictionaries[index]))
                dictionaries.RemoveAt(index);
        }

        if (imageBackgroundStylesEnabled)
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(ImageBackgroundStyleSource, UriKind.Absolute)
            });
            logger.LogDebug("Launcher image background styles applied.");
        }

        application.Resources[SurfaceBackdropBlurEnabledResourceKey] =
            ResolveSurfaceBackdropBlurEnabled(
                imageBackgroundStylesEnabled,
                effectiveThemeOverride ?? EffectiveTheme);
    }

    internal static bool ResolveSurfaceBackdropBlurEnabled(
        bool imageBackgroundStylesEnabled,
        EffectiveTheme effectiveTheme)
    {
        return imageBackgroundStylesEnabled && effectiveTheme is EffectiveTheme.Dark;
    }

    private static string GetThemeSource(EffectiveTheme theme)
    {
        return theme is EffectiveTheme.Light ? LightThemeSource : DarkThemeSource;
    }

    private static string GetAccentThemeSource(string accentColor)
    {
        var normalizedAccentColor = LauncherAccentColors.Normalize(accentColor);
        return $"{AccentThemeSourcePrefix}{normalizedAccentColor}.xaml";
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.ToString();
        return source?.EndsWith("/Resources/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true
            || source?.EndsWith("/Resources/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsAccentDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.ToString();
        return source?.Contains("/Resources/Themes/Accents/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsImageBackgroundStyleDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.ToString();
        return source?.EndsWith(
            "/Resources/Themes/Backgrounds/Image.xaml",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 仅在“跟随系统”模式响应颜色偏好变化，显式主题不应被系统事件覆盖。
        if (!followSystem)
            return;

        if (e.Category is not UserPreferenceCategory.General
            and not UserPreferenceCategory.VisualStyle
            and not UserPreferenceCategory.Color)
        {
            return;
        }

        uiDispatcher.Post(() => ApplyPreference(
            preferredTheme,
            followSystem,
            backgroundOpacityPercent,
            disableBackgroundBlur));
    }
}
