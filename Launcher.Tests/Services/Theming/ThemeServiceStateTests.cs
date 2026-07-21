/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services.Theming;

public sealed class ThemeServiceStateTests
{
    [Fact]
    public void BackgroundEffectIsNormalizedAndPublishesOneCohesiveChange()
    {
        using var service = CreateService();
        var changes = new List<BackgroundEffectChangedEventArgs>();
        service.BackgroundEffectChanged += (_, args) => changes.Add(args);

        service.ApplyBackgroundEffect("image", enableImageControlBlur: true);
        service.ApplyBackgroundEffect("IMAGE", enableImageControlBlur: false);

        Assert.Equal(LauncherBackgroundEffects.Image, service.BackgroundEffect);
        var change = Assert.Single(changes);
        Assert.Equal(LauncherBackgroundEffects.Acrylic, change.OldEffect);
        Assert.Equal(LauncherBackgroundEffects.Image, change.NewEffect);
    }

    [Fact]
    public void ThemeChangeIsPublishedOnlyAfterInitialPreferenceHasBeenApplied()
    {
        using var service = CreateService();
        var changes = new List<EffectiveThemeChangedEventArgs>();
        service.EffectiveThemeChanged += (_, args) => changes.Add(args);

        service.ApplyPreference("Dark", followSystem: false, backgroundOpacityPercent: 42);
        service.ApplyPreference("Light", followSystem: false, backgroundOpacityPercent: 42);

        Assert.Equal(EffectiveTheme.Light, service.EffectiveTheme);
        var change = Assert.Single(changes);
        Assert.Equal(EffectiveTheme.Dark, change.OldTheme);
        Assert.Equal(EffectiveTheme.Light, change.NewTheme);
    }

    private static ThemeService CreateService() => new(
        new ImmediateDispatcher(),
        null,
        new TestProgressiveBlurSupport(),
        new ThemeResourceLayerManager(_ => new System.Windows.ResourceDictionary()));

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool HasAccess => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class TestProgressiveBlurSupport : IProgressiveBlurSupport
    {
        public ProgressiveBlurCapabilitySnapshot Current { get; } = new(
            false,
            0,
            false,
            ProgressiveBlurUnavailableReason.RenderingTierTooLow,
            null);

#pragma warning disable CS0067
        public event EventHandler? AvailabilityChanged;
#pragma warning restore CS0067

        public void Dispose()
        {
        }
    }
}
