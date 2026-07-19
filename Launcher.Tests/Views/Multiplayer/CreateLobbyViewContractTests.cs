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
    public void LanWorldsUseReadOnlyDisplayWithThemeForeground()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "CreateLobbyView.xaml"));
        var display = Assert.Single(view
            .Descendants()
            .Where(element => element.Name.LocalName == "ItemsControl")
            .Where(element => element.Attribute("ItemsSource")?.Value.Contains("LanWorlds", StringComparison.Ordinal) == true));

        Assert.Equal(
            "{StaticResource MultiplayerLanWorldItemTemplate}",
            display.Attribute("ItemTemplate")?.Value);
        var displaySurface = Assert.Single(display.Ancestors()
            .Where(element => element.Name.LocalName == "Border"));
        Assert.Equal(
            "{StaticResource ReadOnlyFieldSurfaceStyle}",
            displaySurface.Attribute("Style")?.Value);
        Assert.Null(displaySurface.Attribute("Height"));
        Assert.DoesNotContain(view.Descendants(), element =>
            element.Attribute("SelectedItem") is not null);

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var itemTemplate = Assert.Single(view
            .Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate")
            .Where(element => element.Attribute(xaml + "Key")?.Value == "MultiplayerLanWorldItemTemplate"));
        var itemText = Assert.Single(itemTemplate
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock"));
        Assert.Equal(
            "{DynamicResource LauncherTextPrimaryBrush}",
            itemText.Attribute("Foreground")?.Value);
    }

    [Fact]
    public void LobbyControlsBindToRealCreationState()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "CreateLobbyView.xaml"));

        Assert.Contains(view.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding CreateLobbyCommand}"
            && element.Attribute("Content")?.Value == "{Binding CreateLobbyButtonText}");
        Assert.Contains(view.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding RoomCode}");
        Assert.Contains(view.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Command")?.Value == "{Binding CopyRoomCodeCommand}");
        Assert.Contains(view.Descendants(), element =>
            element.Name.LocalName == "ListPageItemButton"
            && element.Attribute("Subtitle")?.Value == "{Binding Subtitle}"
            && element.Attribute("TrailingText")?.Value == "{Binding LatencyText}");
        Assert.Contains(view.Descendants(), element =>
            element.Name.LocalName == "AdaptiveTagList"
            && element.Attribute("ItemsSource")?.Value == "{Binding RoleTags}");

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var createdLobbyLayer = Assert.Single(view
            .Descendants()
            .Where(element => element.Attribute(xaml + "Name")?.Value == "CreatedLobbyLayer"));
        var createdLobbyContent = Assert.Single(createdLobbyLayer
            .Elements()
            .Where(element => element.Name.LocalName == "StackPanel"));
        var subtitle = Assert.Single(createdLobbyContent
            .Elements()
            .Where(element => element.Attribute("Text")?.Value == "{x:Static res:Strings.Multiplayer_LobbyPoweredByTerracotta}"));
        var leaveButton = Assert.Single(createdLobbyContent
            .Elements()
            .Where(element => element.Attribute("Command")?.Value == "{Binding RequestLeaveLobbyCommand}"));
        var roomCodeSection = Assert.Single(createdLobbyContent
            .Elements()
            .Where(element => element.Descendants().Any(descendant =>
                descendant.Attribute("Header")?.Value == "{x:Static res:Strings.Multiplayer_LobbyRoomCodeHeader}")));

        Assert.Equal("{StaticResource LauncherDangerDialogButtonStyle}", leaveButton.Attribute("Style")?.Value);
        Assert.Null(leaveButton.Attribute("FontSize"));
        Assert.True(subtitle.IsBefore(leaveButton));
        Assert.True(leaveButton.IsBefore(roomCodeSection));
    }

    [Fact]
    public void CreationInstructionsAppearBeforeLanWorldSelection()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "CreateLobbyView.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var setupLayer = Assert.Single(view
            .Descendants()
            .Where(element => element.Attribute(xaml + "Name")?.Value == "CreateLobbySetupLayer"));
        var sectionHeaders = setupLayer
            .Elements()
            .Where(element => element.Name.LocalName == "GroupBox")
            .Select(element => element.Attribute("Header")?.Value)
            .ToArray();

        Assert.Equal(
            [
                "{x:Static res:Strings.Multiplayer_SectionCreateLobby}",
                "{x:Static res:Strings.Multiplayer_Create_SelectGameInstanceSection}",
            ],
            sectionHeaders);
    }

    [Fact]
    public void TerracottaAttributionIsFixedFiftyPixelsAbovePageBottomAndUsesHyperlink()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Multiplayer",
            "MultiplayerPageView.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageRoot = Assert.Single(view
            .Descendants()
            .Where(element => element.Attribute(xaml + "Name")?.Value == "PageRoot"));
        var attribution = Assert.Single(pageRoot
            .Elements()
            .Where(element => element.Attribute(xaml + "Name")?.Value == "TerracottaAttribution"));
        var hyperlink = Assert.Single(attribution
            .Descendants()
            .Where(element => element.Name.LocalName == "Hyperlink"));

        Assert.Same(pageRoot.Elements().Last(), attribution);
        Assert.Equal("284,0,24,50", attribution.Attribute("Margin")?.Value);
        Assert.Equal("Center", attribution.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Bottom", attribution.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Center", attribution.Attribute("TextAlignment")?.Value);
        Assert.Equal("{Binding OpenTerracottaProjectCommand}", hyperlink.Attribute("Command")?.Value);
        Assert.Equal("{StaticResource MultiplayerProjectHyperlinkStyle}", hyperlink.Attribute("Style")?.Value);
        var conditions = attribution
            .Descendants()
            .Where(element => element.Name.LocalName == "Condition")
            .Select(element => (element.Attribute("Binding")?.Value, element.Attribute("Value")?.Value))
            .ToArray();
        Assert.Contains(("{Binding IsCreateLobbySection}", "True"), conditions);
        Assert.Contains(("{Binding IsLobbyStep}", "False"), conditions);
        Assert.Contains(hyperlink.Descendants(), element =>
            element.Attribute("Text")?.Value
                == "{x:Static res:Strings.Multiplayer_Create_TerracottaAttributionLinkText}");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
