/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views;

public sealed class InstanceResourceCategoryTagContractTests
{
    [Theory]
    [InlineData("InstanceModManagementSettingsView.xaml")]
    [InlineData("InstanceResourcePackManagementSettingsView.xaml")]
    [InlineData("InstanceShaderPackManagementSettingsView.xaml")]
    public void LocalResourceRowsUseAdaptiveTitleTags(string fileName)
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "GameSettings",
            fileName));
        var tags = Assert.Single(document
            .Descendants()
            .Where(element => element.Name.LocalName == "AdaptiveTagList"));

        Assert.Contains("TitleTags", tags.Attribute("ItemsSource")?.Value, StringComparison.Ordinal);
        Assert.Contains("HasTitleTags", tags.Attribute("Visibility")?.Value, StringComparison.Ordinal);
        Assert.Equal("ListPageItemButton.TitleTrailingContent", tags.Parent?.Name.LocalName);
    }

    [Theory]
    [InlineData("InstanceModManagementSettingsView.xaml")]
    [InlineData("InstanceResourcePackManagementSettingsView.xaml")]
    [InlineData("InstanceShaderPackManagementSettingsView.xaml")]
    public void LocalResourceRowsPlaceDisabledAwareInfoButtonBeforeExistingActions(string fileName)
    {
        var document = LoadView(fileName);
        var infoIcon = Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "SvgIcon")
            .Where(element => element.Attribute("IconKey")?.Value == "instance_setting_page/info"));
        var infoButton = Assert.IsType<XElement>(infoIcon.Parent);
        var actionPanel = Assert.IsType<XElement>(infoButton.Parent);

        Assert.Same(infoButton, actionPanel.Elements().First());
        Assert.Contains("HasProjectDetails", infoButton.Attribute("IsEnabled")?.Value, StringComparison.Ordinal);
        Assert.Contains("OpenResourceDetailsCommand", infoButton.Attribute("Command")?.Value, StringComparison.Ordinal);
        Assert.Contains("GameSettings_OpenResourceDetailsTooltip", infoButton.Attribute("ToolTip")?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void InfoIconInheritsButtonForegroundForThemeAndDisabledStates()
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Assets",
            "Icons",
            "instance_setting_page",
            "info.svg");
        var source = File.ReadAllText(path);

        Assert.Contains("currentColor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#333", source, StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument LoadView(string fileName) => XDocument.Load(Path.Combine(
        FindRepositoryRoot().FullName,
        "Launcher.App",
        "Views",
        "GameSettings",
        fileName));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
