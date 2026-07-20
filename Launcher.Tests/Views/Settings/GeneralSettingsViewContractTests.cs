/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Settings;

public sealed class GeneralSettingsViewContractTests
{
    [Theory]
    [InlineData("{x:Static res:Strings.Settings_MinecraftDirectoryLabel}")]
    [InlineData("{x:Static res:Strings.Settings_LauncherLogDirectoryLabel}")]
    public void DirectorySectionsUseRoundedFieldSurfaces(string header)
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Settings",
            "GeneralSettingsView.xaml"));
        var group = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "GroupBox"
            && element.Attribute("Header")?.Value == header));
        var surface = Assert.Single(group.Elements().Where(element =>
            element.Name.LocalName == "Border"));

        Assert.Equal(
            "{StaticResource SectionFieldSurfaceStyle}",
            surface.Attribute("Style")?.Value);
    }

    [Fact]
    public void DiagnosticLoggingRowHasNoNestedSurfaceOrDescription()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Settings",
            "GeneralSettingsView.xaml"));
        var launcherLogSection = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "GroupBox"
            && element.Attribute("Header")?.Value ==
                "{x:Static res:Strings.Settings_LauncherLogDirectoryLabel}"));
        var label = Assert.Single(launcherLogSection.Descendants().Where(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value ==
                "{x:Static res:Strings.Settings_DiagnosticLoggingLabel}"));

        Assert.Equal("Grid", label.Parent?.Name.LocalName);
        Assert.DoesNotContain(launcherLogSection.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value ==
                "{x:Static res:Strings.Settings_DiagnosticLoggingDescription}");
        Assert.Single(launcherLogSection.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Style")?.Value ==
                "{StaticResource SectionFieldSurfaceStyle}"));

        var openDirectoryButton = Assert.Single(launcherLogSection.Descendants().Where(element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value ==
                "{Binding OpenLauncherLogDirectoryCommand}"));
        var contentPanel = Assert.IsType<XElement>(label.Parent?.Parent);
        Assert.True(
            contentPanel.Elements().ToList().IndexOf(openDirectoryButton.Parent!)
            < contentPanel.Elements().ToList().IndexOf(label.Parent!));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
