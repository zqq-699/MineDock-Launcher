/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Multiplayer;

public sealed class CreateLobbyViewContractTests
{
    [Fact]
    public void LanWorldSelectorUsesDisplayTemplateForListAndSelection()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "CreateLobbyView.xaml"));
        var selector = Assert.Single(view
            .Descendants()
            .Where(element => element.Name.LocalName == "AnimatedComboBox")
            .Where(element => element.Attribute("ItemsSource")?.Value.Contains("LanWorlds", StringComparison.Ordinal) == true));

        Assert.Equal(
            "{StaticResource MultiplayerLanWorldItemTemplate}",
            selector.Attribute("ItemTemplate")?.Value);
        Assert.Equal(
            "{StaticResource MultiplayerLanWorldItemTemplate}",
            selector.Attribute("SelectionItemTemplate")?.Value);

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var itemTemplate = Assert.Single(view
            .Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate")
            .Where(element => element.Attribute(xaml + "Key")?.Value == "MultiplayerLanWorldItemTemplate"));
        var itemText = Assert.Single(itemTemplate
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock"));
        Assert.Equal(
            "{DynamicResource Brush.F4F4F4}",
            itemText.Attribute("Foreground")?.Value);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
