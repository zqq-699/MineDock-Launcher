/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Settings;

public sealed class ThemeSettingsViewContractTests
{
    [Fact]
    public void LauncherBackgroundEffectUsesRequiredComboBoxBinding()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Settings",
            "ThemeSettingsView.xaml"));

        var comboBox = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "AnimatedComboBox"
            && (string?)element.Attribute("ItemsSource") == "{Binding BackgroundEffectOptions}"));

        Assert.Equal(
            "{Binding SelectedBackgroundEffectOption, Mode=TwoWay}",
            (string?)comboBox.Attribute("SelectedItem"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "CheckBox"
            && ((string?)element.Attribute("IsChecked"))?.Contains("DisableBackgroundBlur", StringComparison.Ordinal) == true);

        var opacitySlider = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Slider"
            && ((string?)element.Attribute("Value"))?.Contains("LauncherBackgroundOpacityPercent", StringComparison.Ordinal) == true));
        Assert.Contains(opacitySlider.Ancestors(), element =>
            element.Name.LocalName == "Grid"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith(".IsExpanded", StringComparison.Ordinal)
                && attribute.Value == "{Binding IsBackgroundOpacityVisible}"));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
