/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services.Theming;

public sealed class ThemeResourceLayerManagerTests
{
    [Fact]
    public void ApplyLayersPreservesBaseResourcesAndKeepsManagedLayersCanonical()
    {
        RunOnStaThread(() =>
        {
            var createdSources = new List<string>();
            var manager = new ThemeResourceLayerManager(source =>
            {
                createdSources.Add(source);
                var dictionary = new ResourceDictionary { ["SourceMarker"] = source };
                return dictionary;
            });
            var resources = new ResourceDictionary();
            var shared = new ResourceDictionary { ["Layer"] = "Shared" };
            var controls = new ResourceDictionary { ["Layer"] = "ControlStyles" };
            resources.MergedDictionaries.Add(shared);
            resources.MergedDictionaries.Add(controls);

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Dark,
                LauncherAccentColors.Blue,
                imageBackgroundEnabled: false,
                imageControlBlurEnabled: false);

            Assert.Equal(4, resources.MergedDictionaries.Count);
            Assert.Same(shared, resources.MergedDictionaries[0]);
            Assert.Same(controls, resources.MergedDictionaries[1]);
            var darkLayer = resources.MergedDictionaries[2];
            var blueLayer = resources.MergedDictionaries[3];
            Assert.EndsWith("/Resources/Themes/Dark.xaml", GetSourceMarker(darkLayer), StringComparison.Ordinal);
            Assert.EndsWith("/Resources/Themes/Accents/Blue.xaml", GetSourceMarker(blueLayer), StringComparison.Ordinal);
            Assert.DoesNotContain(createdSources, source =>
                source.EndsWith("/Resources/Themes/Backgrounds/Image.xaml", StringComparison.Ordinal));

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Dark,
                LauncherAccentColors.Blue,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: false);

            Assert.Equal(5, resources.MergedDictionaries.Count);
            Assert.Same(darkLayer, resources.MergedDictionaries[2]);
            Assert.Same(blueLayer, resources.MergedDictionaries[3]);
            var imageLayer = resources.MergedDictionaries[4];
            Assert.EndsWith("/Resources/Themes/Backgrounds/Image.xaml", GetSourceMarker(imageLayer), StringComparison.Ordinal);
            Assert.DoesNotContain(createdSources, source =>
                source.EndsWith("/Resources/Themes/Backgrounds/ImageBlur.xaml", StringComparison.Ordinal));

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Dark,
                LauncherAccentColors.Blue,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: true);

            Assert.Equal(6, resources.MergedDictionaries.Count);
            Assert.Same(imageLayer, resources.MergedDictionaries[4]);
            var imageBlurLayer = resources.MergedDictionaries[5];
            Assert.EndsWith(
                "/Resources/Themes/Backgrounds/ImageBlur.xaml",
                GetSourceMarker(imageBlurLayer),
                StringComparison.Ordinal);

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Light,
                LauncherAccentColors.Purple,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: true);

            Assert.Equal(6, resources.MergedDictionaries.Count);
            Assert.Same(shared, resources.MergedDictionaries[0]);
            Assert.Same(controls, resources.MergedDictionaries[1]);
            Assert.EndsWith("/Resources/Themes/Light.xaml", GetSourceMarker(resources.MergedDictionaries[2]), StringComparison.Ordinal);
            Assert.EndsWith("/Resources/Themes/Accents/Purple.xaml", GetSourceMarker(resources.MergedDictionaries[3]), StringComparison.Ordinal);
            Assert.Same(imageLayer, resources.MergedDictionaries[4]);
            Assert.Same(imageBlurLayer, resources.MergedDictionaries[5]);
            Assert.Equal(resources.MergedDictionaries.Count, resources.MergedDictionaries.Distinct().Count());

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Light,
                LauncherAccentColors.Purple,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: false);

            Assert.Equal(5, resources.MergedDictionaries.Count);
            Assert.Same(imageLayer, resources.MergedDictionaries[4]);
            Assert.DoesNotContain(imageBlurLayer, resources.MergedDictionaries);
        });
    }

    [Fact]
    public void ThemeSwitchReplacesImageDimBrushWithCurrentThemeColor()
    {
        RunOnStaThread(() =>
        {
            var manager = new ThemeResourceLayerManager();
            var resources = new ResourceDictionary();
            var target = new Border { Resources = resources };
            target.SetResourceReference(
                Border.BackgroundProperty,
                "Brush.LauncherBackground.Image.DimOverlay");

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Dark,
                LauncherAccentColors.Blue,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: true);

            var darkBrush = Assert.IsType<SolidColorBrush>(target.Background);
            Assert.Equal(Colors.Black, darkBrush.Color);

            manager.ApplyLayers(
                resources,
                EffectiveTheme.Light,
                LauncherAccentColors.Blue,
                imageBackgroundEnabled: true,
                imageControlBlurEnabled: true);

            var lightBrush = Assert.IsType<SolidColorBrush>(target.Background);
            Assert.NotSame(darkBrush, lightBrush);
            Assert.Equal(Colors.White, lightBrush.Color);
        });
    }

    private static string GetSourceMarker(ResourceDictionary dictionary) =>
        Assert.IsType<string>(dictionary["SourceMarker"]);

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
