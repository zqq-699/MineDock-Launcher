/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ThemeResourceContractTests
{
    [Fact]
    public void DarkAndLightThemesExposeTheSameCoreResources()
    {
        var dark = LoadKeys("Dark.xaml");
        var light = LoadKeys("Light.xaml");

        Assert.Equal(dark.Order(), light.Order());
        Assert.Contains("Brush.Text.Primary", dark);
        Assert.Contains("Brush.Surface.Window", dark);
        Assert.Contains("Brush.Control.Border", dark);
        Assert.Contains("Color.Surface.Popup", dark);
        Assert.Contains("Color.Page.Background", dark);
    }

    [Fact]
    public void LightThemeUsesRequiredPageAndCardBackgroundColors()
    {
        var document = Load("Light.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageBackground = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Page.Background"));
        var cardSurface = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Card.Surface"));

        Assert.Equal("#EEEEF0", pageBackground.Value);
        Assert.Equal("#B3FFFFFF", cardSurface.Value);
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void ThemeDefinesSharedCardSurfaceShadow(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shadow = Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "DropShadowEffect"
                && element.Attribute(xaml + "Key")?.Value == "Effect.Card.Surface"));

        Assert.Equal("15", shadow.Attribute("BlurRadius")?.Value);
        Assert.Equal("270", shadow.Attribute("Direction")?.Value);
        Assert.Equal("0.15", shadow.Attribute("Opacity")?.Value);
        Assert.Equal("2", shadow.Attribute("ShadowDepth")?.Value);
    }

    [Theory]
    [InlineData("Dark.xaml", "#FF252525", "#8A252525")]
    [InlineData("Light.xaml", "#FFFFFFFF", "#8AFFFFFF")]
    public void ThemeDefinesBackdropBlurBaseAndTint(
        string fileName,
        string expectedBase,
        string expectedTint)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertColorAndBrush(document, xaml, "Backdrop.Base", expectedBase);
        AssertColorAndBrush(document, xaml, "Backdrop.Tint", expectedTint);
    }

    [Theory]
    [InlineData("Dark.xaml", "#90000000")]
    [InlineData("Light.xaml", "#80FFFFFF")]
    public void ThemeDefinesSecondaryMenuBackdropTint(string fileName, string expectedTint)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        AssertColorAndBrush(document, xaml, "SecondaryMenu.BackdropTint", expectedTint);
    }

    [Theory]
    [InlineData("Dark.xaml", "#18FFFFFF")]
    [InlineData("Light.xaml", "#00FFFFFF")]
    public void ThemeDefinesSurfaceBackdropContrastOverlay(string fileName, string expectedOverlay)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        AssertColorAndBrush(document, xaml, "SurfaceBackdrop.ContrastOverlay", expectedOverlay);
    }

    [Theory]
    [InlineData("Dark.xaml", "#38FFFFFF")]
    [InlineData("Light.xaml", "#30000000")]
    public void ThemeDefinesDistinctDisabledButtonSurface(string fileName, string expectedColor)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        AssertColorAndBrush(document, xaml, "Button.Secondary.Disabled", expectedColor);
    }

    private static void AssertColorAndBrush(
        XDocument document,
        XNamespace xaml,
        string suffix,
        string expectedColor)
    {
        var colorKey = $"Color.{suffix}";
        var brushKey = $"Brush.{suffix}";
        var color = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == colorKey));
        var brush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == brushKey));

        Assert.Equal(expectedColor, color.Value);
        Assert.Equal($"{{StaticResource {colorKey}}}", brush.Attribute("Color")?.Value);
    }

    private static HashSet<string> LoadKeys(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => element.Attribute(xaml + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XDocument Load(string fileName) =>
        XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Resources",
            "Themes",
            fileName));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
