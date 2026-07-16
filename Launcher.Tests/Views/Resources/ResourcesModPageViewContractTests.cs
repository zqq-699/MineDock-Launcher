/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;
using Launcher.App.ViewModels.Resources;
using Launcher.Domain.Models;

namespace Launcher.Tests.Views.Resources;

public sealed class ResourcesModPageViewContractTests
{
    [Theory]
    [InlineData(ResourceProjectKind.Mod, true)]
    [InlineData(ResourceProjectKind.ResourcePack, false)]
    [InlineData(ResourceProjectKind.ShaderPack, false)]
    [InlineData(ResourceProjectKind.World, false)]
    [InlineData(ResourceProjectKind.Modpack, true)]
    public void LoaderDetailsAreOnlyAvailableForModsAndModpacks(ResourceProjectKind kind, bool expected)
    {
        var project = new ResourceProject
        {
            Kind = kind
        };

        var item = new ResourcesModProjectItemViewModel(project);

        Assert.Equal(expected, item.ShowsLoaders);
    }

    [Fact]
    public void LoaderVisibilityBindingBelongsToLoaderDetailRow()
    {
        var document = LoadView();

        var loaderRow = FindDetailRow(document, "Resources_ModDetailsLoaderLabel");
        var versionRow = FindDetailRow(document, "Resources_ModDetailsVersionLabel");

        Assert.Contains("ShowsLoaders", loaderRow.Attribute("Visibility")?.Value, StringComparison.Ordinal);
        Assert.Null(versionRow.Attribute("Visibility"));
    }

    [Theory]
    [InlineData("ProjectList.LoadingMessage", "0,172,12,0")]
    [InlineData("ProjectList.EmptyMessage", "0,172,12,0")]
    [InlineData("ProjectList.LoadErrorMessage", "0,172,12,0")]
    [InlineData("Versions.LoadingMessage", "0,172,12,64")]
    [InlineData("Versions.EmptyMessage", "0,172,12,64")]
    [InlineData("Versions.LoadErrorMessage", "0,172,12,64")]
    public void ListStateMessagesAreCenteredWithinTheListViewport(string bindingPath, string expectedMargin)
    {
        var message = Assert.Single(LoadView()
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains(bindingPath, StringComparison.Ordinal) == true));

        Assert.Equal(expectedMargin, message.Attribute("Margin")?.Value);
    }

    private static XElement FindDetailRow(XDocument document, string labelKey)
    {
        var label = Assert.Single(document
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains(labelKey, StringComparison.Ordinal) == true));

        return label.Ancestors().First(element => element.Name.LocalName == "Grid");
    }

    private static XDocument LoadView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot().FullName,
        "Launcher.App",
        "Views",
        "Resources",
        "ResourcesModPageView.xaml"));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
