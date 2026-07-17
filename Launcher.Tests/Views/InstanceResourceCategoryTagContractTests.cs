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

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
