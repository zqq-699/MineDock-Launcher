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

        var imageSelection = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Grid"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith(".IsExpanded", StringComparison.Ordinal)
                && attribute.Value == "{Binding IsBackgroundImageSelectionVisible}")
            && element.Descendants().Any(descendant =>
                descendant.Attribute("Command")?.Value ==
                    "{Binding OpenLauncherBackgroundImageFolderCommand}")));
        var openFolderButton = Assert.Single(imageSelection.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding OpenLauncherBackgroundImageFolderCommand}"));
        var refreshButton = Assert.Single(imageSelection.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding RefreshLauncherBackgroundImageCommand}"));
        var clearButton = Assert.Single(imageSelection.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding ClearLauncherBackgroundImagesCommand}"));

        Assert.DoesNotContain(imageSelection.Descendants(), element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Style")?.Value == "{StaticResource ReadOnlyFieldSurfaceStyle}");
        Assert.Equal("1", openFolderButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("{StaticResource LauncherDialogButtonStyle}", openFolderButton.Attribute("Style")?.Value);
        Assert.Equal("2", refreshButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("{StaticResource LauncherDialogButtonStyle}", refreshButton.Attribute("Style")?.Value);
        Assert.Equal("3", clearButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("{StaticResource LauncherDangerDialogButtonStyle}", clearButton.Attribute("Style")?.Value);

        var controlBlurToggle = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "CheckBox"
            && element.Attribute("IsChecked")?.Value ==
                "{Binding EnableImageBackgroundControlBlur, Mode=TwoWay}"));
        Assert.Contains(controlBlurToggle.Ancestors(), element =>
            element.Name.LocalName == "Grid"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith(".IsExpanded", StringComparison.Ordinal)
                && attribute.Value == "{Binding IsBackgroundImageSelectionVisible}"));
        Assert.Equal(
            "{StaticResource LauncherSwitchToggleButtonStyle}",
            controlBlurToggle.Attribute("Style")?.Value);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
