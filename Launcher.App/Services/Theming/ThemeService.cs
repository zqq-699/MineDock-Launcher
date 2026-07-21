/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Launcher.App.Services;

/// <summary>
/// Maintains launcher appearance preferences and publishes effective theme and background-effect changes.
/// Resource ordering and progressive-blur capability handling are delegated to dedicated collaborators.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<ThemeService> logger;
    private readonly ThemeResourceLayerManager resourceLayerManager;
    private readonly ProgressiveBlurController progressiveBlurController;
    private string preferredTheme = LauncherDefaults.DefaultTheme;
    private string preferredAccentColor = LauncherDefaults.DefaultAccentColor;
    private string backgroundEffect = LauncherDefaults.DefaultLauncherBackgroundEffect;
    private bool followSystem = true;
    private int backgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;
    private bool enableImageControlBlur = LauncherDefaults.DefaultEnableImageBackgroundControlBlur;
    private bool hasAppliedTheme;
    private bool isDisposed;

    public ThemeService(IUiDispatcher uiDispatcher, ILogger<ThemeService>? logger = null)
        : this(
            uiDispatcher,
            logger,
            new WpfProgressiveBlurSupport(),
            new ThemeResourceLayerManager())
    {
    }

    internal ThemeService(
        IUiDispatcher uiDispatcher,
        ILogger<ThemeService>? logger,
        IProgressiveBlurSupport progressiveBlurSupport,
        ThemeResourceLayerManager resourceLayerManager)
    {
        this.uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        this.logger = logger ?? NullLogger<ThemeService>.Instance;
        this.resourceLayerManager = resourceLayerManager
            ?? throw new ArgumentNullException(nameof(resourceLayerManager));
        progressiveBlurController = new ProgressiveBlurController(
            this.uiDispatcher,
            progressiveBlurSupport ?? throw new ArgumentNullException(nameof(progressiveBlurSupport)),
            this.logger);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public EffectiveTheme EffectiveTheme { get; private set; } = EffectiveTheme.Dark;

    public string BackgroundEffect => backgroundEffect;

    public event EventHandler<EffectiveThemeChangedEventArgs>? EffectiveThemeChanged;

    public event EventHandler<BackgroundEffectChangedEventArgs>? BackgroundEffectChanged;

    public void ApplyPreference(
        string? theme,
        bool followSystem,
        int backgroundOpacityPercent)
    {
        preferredTheme = NormalizeTheme(theme);
        this.followSystem = followSystem;
        this.backgroundOpacityPercent = NormalizeBackgroundOpacity(backgroundOpacityPercent);

        var oldTheme = EffectiveTheme;
        var nextTheme = ResolveEffectiveTheme(preferredTheme, followSystem);
        EffectiveTheme = nextTheme;
        uiDispatcher.Invoke(() =>
        {
            ApplyAppearanceResourcesCore();
            progressiveBlurController.Initialize();
        });

        if (!hasAppliedTheme)
        {
            hasAppliedTheme = true;
            return;
        }

        if (oldTheme != nextTheme)
            EffectiveThemeChanged?.Invoke(this, new EffectiveThemeChangedEventArgs(oldTheme, nextTheme));
    }

    public void ApplyAccent(string? accentColor)
    {
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
        uiDispatcher.Invoke(ApplyAppearanceResourcesCore);
    }

    public void ApplyBackgroundOpacity(int opacityPercent)
    {
        backgroundOpacityPercent = NormalizeBackgroundOpacity(opacityPercent);
        uiDispatcher.Invoke(ApplyPageBackgroundOpacityCore);
    }

    public void ApplyBackgroundEffect(string? backgroundEffect, bool enableImageControlBlur)
    {
        var oldEffect = this.backgroundEffect;
        var nextEffect = LauncherBackgroundEffects.Normalize(backgroundEffect);
        this.backgroundEffect = nextEffect;
        this.enableImageControlBlur = enableImageControlBlur;
        uiDispatcher.Invoke(ApplyAppearanceResourcesCore);

        if (!string.Equals(oldEffect, nextEffect, StringComparison.Ordinal))
        {
            BackgroundEffectChanged?.Invoke(
                this,
                new BackgroundEffectChangedEventArgs(oldEffect, nextEffect));
        }
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        progressiveBlurController.Dispose();
        isDisposed = true;
    }

    private void ApplyAppearanceResourcesCore()
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var presentation = LauncherBackgroundPresentationPolicy.Resolve(
            backgroundEffect,
            backgroundOpacityPercent,
            enableImageControlBlur);
        resourceLayerManager.ApplyLayers(
            application.Resources,
            EffectiveTheme,
            preferredAccentColor,
            presentation.IsImageBackgroundEnabled,
            presentation.IsImageControlBlurEnabled);
        ThemeResourceLayerManager.ApplyPageBackgroundOpacity(
            application.Resources,
            presentation.PageBackgroundOpacityPercent);
        logger.LogDebug(
            "Launcher appearance resources applied. Theme={Theme} AccentColor={AccentColor} BackgroundEffect={BackgroundEffect} ImageLayerEnabled={ImageLayerEnabled} ImageControlBlurEnabled={ImageControlBlurEnabled}",
            EffectiveTheme,
            preferredAccentColor,
            presentation.Effect,
            presentation.IsImageBackgroundEnabled,
            presentation.IsImageControlBlurEnabled);
    }

    private void ApplyPageBackgroundOpacityCore()
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var presentation = LauncherBackgroundPresentationPolicy.Resolve(
            backgroundEffect,
            backgroundOpacityPercent,
            enableImageControlBlur);
        ThemeResourceLayerManager.ApplyPageBackgroundOpacity(
            application.Resources,
            presentation.PageBackgroundOpacityPercent);
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

    private static int NormalizeBackgroundOpacity(int opacityPercent) => Math.Clamp(opacityPercent, 0, 100);

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
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
            backgroundOpacityPercent));
    }
}
