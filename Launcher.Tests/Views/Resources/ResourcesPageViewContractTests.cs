/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;
using Launcher.App.Controls;

namespace Launcher.Tests.Views.Resources;

public sealed class ResourcesPageViewContractTests
{
    [Fact]
    public void ListPageFramePlacesOptionalLeadingContentBeforeSearchBox()
    {
        Assert.Equal(typeof(object), ListPageFrame.SearchLeadingContentProperty.PropertyType);
        Assert.Equal(new System.Windows.CornerRadius(8), ListPageFrame.SearchBoxCornerRadiusProperty.DefaultMetadata.DefaultValue);

        var searchBox = Assert.Single(LoadListPageFrame()
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBox")
            .Where(element => element.Attribute("Text")?.Value.Contains("SearchText", StringComparison.Ordinal) == true));
        var searchRow = Assert.IsType<XElement>(searchBox.Parent);
        var children = searchRow.Elements().ToList();
        var leadingContent = Assert.Single(children
            .Where(element => element.Name.LocalName == "ContentPresenter")
            .Where(element => element.Attribute("Content")?.Value.Contains("SearchLeadingContent", StringComparison.Ordinal) == true));

        Assert.True(children.IndexOf(leadingContent) < children.IndexOf(searchBox));
        Assert.Equal("0", leadingContent.Attribute("Grid.Column")?.Value);
        Assert.Equal("1", searchBox.Attribute("Grid.Column")?.Value);
    }

    [Fact]
    public void ProjectListUsesLeadingFilterButtonAndOnlyVersionsUseFilterRow()
    {
        var view = LoadResourcesPage();
        var frame = Assert.Single(view.Descendants()
            .Where(element => element.Name.LocalName == "ListPageFrame"));
        var button = Assert.Single(view.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attribute("Content")?.Value.Contains("Resources_ModFilterButtonLabel", StringComparison.Ordinal) == true));

        Assert.Equal("{Binding ProjectList.OpenFilterDialogCommand}", button.Attribute("Command")?.Value);
        Assert.Contains(button.Ancestors(), element =>
            element.Name.LocalName == "DataTemplate"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "ResourcesProjectFilterButtonTemplate"));
        Assert.Contains("CurrentOnlineProjectPage", frame.Attribute("SearchLeadingContent")?.Value, StringComparison.Ordinal);
        Assert.Contains("ResourcesProjectFilterButtonTemplate", frame.Attribute("SearchLeadingContentTemplate")?.Value, StringComparison.Ordinal);
        Assert.Contains("CurrentOnlineProjectPage.IsProjectListStep", frame.Attribute("IsSearchLeadingContentVisible")?.Value, StringComparison.Ordinal);
        Assert.Contains("CurrentOnlineProjectPage.IsProjectVersionsStep", frame.Attribute("IsSearchFilterVisible")?.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(view.Descendants(), element => element.Name.LocalName == "ResourcesModFilterView");
        Assert.Single(view.Descendants().Where(element => element.Name.LocalName == "ResourcesModVersionFilterView"));
    }

    [Fact]
    public void VersionFilterRowKeepsVersionAndLoaderSelectors()
    {
        var combos = LoadVersionFilter()
            .Descendants()
            .Where(element => element.Name.LocalName == "AnimatedComboBox")
            .ToList();

        Assert.Equal(2, combos.Count);
        Assert.Contains(combos, combo => combo.Attribute("ItemsSource")?.Value.Contains("Versions.VersionFilterOptions", StringComparison.Ordinal) == true);
        Assert.Contains(combos, combo => combo.Attribute("ItemsSource")?.Value.Contains("Versions.LoaderFilterOptions", StringComparison.Ordinal) == true);
    }

    private static XDocument LoadResourcesPage() => LoadView("Launcher.App", "Views", "Resources", "ResourcesPageView.xaml");

    private static XDocument LoadVersionFilter() => LoadView("Launcher.App", "Views", "Resources", "ResourcesModVersionFilterView.xaml");

    private static XDocument LoadListPageFrame() => LoadView("Launcher.App", "Controls", "Lists", "ListPageFrame.xaml");

    private static XDocument LoadView(params string[] pathParts) => XDocument.Load(Path.Combine(
        [FindRepositoryRoot().FullName, .. pathParts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
