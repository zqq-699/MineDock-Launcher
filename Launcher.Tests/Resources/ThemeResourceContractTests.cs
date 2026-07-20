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

        var baseColor = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == "Color.Backdrop.Base"));
        var tintColor = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == "Color.Backdrop.Tint"));
        var baseBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.Backdrop.Base"));
        var tintBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.Backdrop.Tint"));

        Assert.Equal(expectedBase, baseColor.Value);
        Assert.Equal(expectedTint, tintColor.Value);
        Assert.Equal("{StaticResource Color.Backdrop.Base}", baseBrush.Attribute("Color")?.Value);
        Assert.Equal("{StaticResource Color.Backdrop.Tint}", tintBrush.Attribute("Color")?.Value);
    }

    [Theory]
    [InlineData("Dark.xaml", "#80111111")]
    [InlineData("Light.xaml", "#80FFFFFF")]
    public void ThemeDefinesSecondaryMenuBackdropTint(string fileName, string expectedTint)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tintColor = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == "Color.SecondaryMenu.BackdropTint"));
        var tintBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.SecondaryMenu.BackdropTint"));

        Assert.Equal(expectedTint, tintColor.Value);
        Assert.Equal(
            "{StaticResource Color.SecondaryMenu.BackdropTint}",
            tintBrush.Attribute("Color")?.Value);
    }

    [Theory]
    [InlineData("Dark.xaml", "#18FFFFFF")]
    [InlineData("Light.xaml", "#00FFFFFF")]
    public void ThemeDefinesSurfaceBackdropContrastOverlay(string fileName, string expectedOverlay)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var overlayColor = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == "Color.SurfaceBackdrop.ContrastOverlay"));
        var overlayBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.SurfaceBackdrop.ContrastOverlay"));

        Assert.Equal(expectedOverlay, overlayColor.Value);
        Assert.Equal(
            "{StaticResource Color.SurfaceBackdrop.ContrastOverlay}",
            overlayBrush.Attribute("Color")?.Value);
    }

    [Theory]
    [InlineData("Dark.xaml", "#38FFFFFF")]
    [InlineData("Light.xaml", "#30000000")]
    public void ThemeDefinesDistinctDisabledButtonSurface(string fileName, string expectedColor)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var color = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value == "Color.Button.Secondary.Disabled"));
        var brush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.Button.Secondary.Disabled"));

        Assert.Equal(expectedColor, color.Value);
        Assert.Equal(
            "{StaticResource Color.Button.Secondary.Disabled}",
            brush.Attribute("Color")?.Value);
    }

    [Fact]
    public void ImageBackgroundModeInheritsAllSharedAndControlStylesInOrder()
    {
        var document = Load(Path.Combine("Backgrounds", "Image.xaml"));
        var sources = document.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        Assert.Equal(
            [
                "pack://application:,,,/BlockHelm_Launcher_x64;component/Resources/Themes/Shared.xaml",
                "pack://application:,,,/BlockHelm_Launcher_x64;component/Styles/ControlStyles.xaml"
            ],
            sources);
    }

    [Fact]
    public void ImageBackgroundModeDefinesNonHomeDimmingResources()
    {
        var document = Load(Path.Combine("Backgrounds", "Image.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dimBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush"
            && element.Attribute(xaml + "Key")?.Value == "Brush.LauncherBackground.Image.DimOverlay"));
        var dimOpacity = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Double"
            && element.Attribute(xaml + "Key")?.Value == "Opacity.LauncherBackground.Image.NonHomeDim"));

        Assert.Equal("#FF000000", dimBrush.Attribute("Color")?.Value);
        Assert.Equal("0.35", dimOpacity.Value);
    }

    [Fact]
    public void ImageBackgroundModeAloneEnablesSecondaryMenuBackdropBlur()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shared = Load("Shared.xaml");
        var image = Load(Path.Combine("Backgrounds", "Image.xaml"));
        var sharedSwitch = Assert.Single(shared.Descendants().Where(element =>
            element.Name.LocalName == "Boolean"
            && element.Attribute(xaml + "Key")?.Value == "Is.SecondaryMenu.BackdropBlur.Enabled"));
        var imageSwitch = Assert.Single(image.Descendants().Where(element =>
            element.Name.LocalName == "Boolean"
            && element.Attribute(xaml + "Key")?.Value == "Is.SecondaryMenu.BackdropBlur.Enabled"));

        Assert.Equal("False", sharedSwitch.Value);
        Assert.Equal("True", imageSwitch.Value);
    }

    [Fact]
    public void SharedThemeDefaultsSurfaceBackdropBlurToDisabled()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shared = Load("Shared.xaml");
        var surfaceSwitch = Assert.Single(shared.Descendants().Where(element =>
            element.Name.LocalName == "Boolean"
            && element.Attribute(xaml + "Key")?.Value == "Is.Surface.BackdropBlur.Enabled"));

        Assert.Equal("False", surfaceSwitch.Value);
    }

    private static HashSet<string> LoadKeys(string fileName)
    {
        var document = Load(fileName);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XDocument Load(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root.FullName, "Launcher.App", "Resources", "Themes", relativePath));
    }
}
