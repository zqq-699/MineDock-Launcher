/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ImageBackgroundResourceContractTests
{
    [Fact]
    public void ImageLayersSeparateAlwaysOnTintFromOptionalControlBlur()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        var imageBlur = LoadTheme("Backgrounds", "ImageBlur.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var layer in new[] { image, imageBlur })
        {
            Assert.Empty(layer.Descendants().Where(element =>
                element.Name.LocalName == "ResourceDictionary"
                && element.Attribute("Source") is not null));
        }

        var blurKeys = new[]
        {
            "Is.SecondaryMenu.BackdropBlur.Enabled",
            "Is.Surface.BackdropBlur.Enabled"
        };
        foreach (var key in blurKeys)
        {
            Assert.DoesNotContain(image.Descendants(), element =>
                element.Attribute(xaml + "Key")?.Value == key);
            var value = Assert.Single(imageBlur.Descendants().Where(element =>
                element.Name.LocalName == "Boolean"
                && element.Attribute(xaml + "Key")?.Value == key));
            Assert.Equal("True", value.Value);
        }

        var imageTint = FindResource(
            image,
            xaml,
            "Boolean",
            "Is.ImageBackground.ControlTint.Enabled");
        Assert.Equal("True", imageTint.Value);

        var shared = LoadTheme("Shared.xaml");
        foreach (var key in blurKeys.Append("Is.ImageBackground.ControlTint.Enabled"))
        {
            var value = Assert.Single(shared.Descendants().Where(element =>
                element.Name.LocalName == "Boolean"
                && element.Attribute(xaml + "Key")?.Value == key));
            Assert.Equal("False", value.Value);
        }
    }

    [Fact]
    public void BackdropStylesKeepImageTintWhenBlurSamplingIsDisabled()
    {
        var effects = LoadAppXaml("Styles", "ControlStyles.Effects.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var styleKey in new[]
                 {
                     "SurfaceBackdropBlurStyle",
                     "SecondaryMenuBackdropStyle",
                     "PrimaryMenuBackdropStyle"
                 })
        {
            var style = FindStyle(effects, xaml, styleKey);
            Assert.Contains(style.Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "IsTintEnabled"
                && element.Attribute("Value")?.Value ==
                    "{DynamicResource Is.ImageBackground.ControlTint.Enabled}");
            Assert.Contains(style.Descendants(), element =>
                element.Name.LocalName == "Trigger"
                && element.Attribute("Property")?.Value == "IsTintEnabled"
                && element.Attribute("Value")?.Value == "True");
        }
    }

    [Theory]
    [InlineData("Dark.xaml", "#FF000000")]
    [InlineData("Light.xaml", "#FFFFFFFF")]
    public void ThemeDefinesImageNonHomeDimColor(string fileName, string expectedColor)
    {
        var document = LoadTheme(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var color = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Color"
            && element.Attribute(xaml + "Key")?.Value ==
                "Color.LauncherBackground.Image.DimOverlay"));
        Assert.Equal(expectedColor, color.Value);

        var brush = FindResource(
            document,
            xaml,
            "SolidColorBrush",
            "Brush.LauncherBackground.Image.DimOverlay");
        Assert.Equal(
            "{StaticResource Color.LauncherBackground.Image.DimOverlay}",
            brush.Attribute("Color")?.Value);
    }

    [Fact]
    public void ImageLayerPreservesNonHomeDimmingAndWindowBorderValues()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dimOpacity = FindResource(image, xaml, "Double", "Opacity.LauncherBackground.Image.NonHomeDim");
        var windowBorder = FindResource(image, xaml, "SolidColorBrush", "Brush.Surface.WindowBorder");
        var windowBorderThickness = FindResource(image, xaml, "Thickness", "Thickness.Surface.WindowBorder");

        Assert.DoesNotContain(image.Descendants(), element =>
            element.Attribute(xaml + "Key")?.Value == "Brush.LauncherBackground.Image.DimOverlay");
        Assert.Equal("0.40", dimOpacity.Value);
        Assert.Equal("Transparent", windowBorder.Attribute("Color")?.Value);
        Assert.Equal("0", windowBorderThickness.Value);
    }

    [Fact]
    public void ImageLayerForcesHomeNamesAndGameSettingsStatesToWhite()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var key in HomeForegroundKeys)
        {
            var brush = FindResource(image, xaml, "SolidColorBrush", key);
            Assert.Equal("{DynamicResource Color.FFFFFF}", brush.Attribute("Color")?.Value);
        }

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        AssertStyleSetter(page, xaml, "HomeAccountNameTextStyle", "{DynamicResource Brush.Home.Name.Foreground}");
        AssertStyleSetter(page, xaml, "HomeVersionNameTextStyle", "{DynamicResource Brush.Home.Name.Foreground}");
        AssertStyleSetter(page, xaml, "HomeChangeVersionButtonStyle", "{DynamicResource Brush.Home.GameSettings.Foreground}");
        var gameSettingsStyle = FindStyle(page, xaml, "HomeChangeVersionButtonStyle");
        Assert.Contains(gameSettingsStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.Home.GameSettings.ForegroundHover}");
        Assert.Contains(gameSettingsStyle.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Value")?.Value == "{DynamicResource Brush.Home.GameSettings.ForegroundPressed}");
    }

    [Fact]
    public void HomeNameShadowIsEnabledOnlyByImageLayer()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shared = LoadTheme("Shared.xaml");
        var defaultEffect = FindResource(
            shared,
            xaml,
            "Null",
            "Effect.Home.ForegroundTextShadow");
        Assert.NotNull(defaultEffect);

        var image = LoadTheme("Backgrounds", "Image.xaml");
        var imageEffect = FindResource(
            image,
            xaml,
            "DropShadowEffect",
            "Effect.Home.ForegroundTextShadow");
        Assert.Equal("False", imageEffect.Attribute(xaml + "Shared")?.Value);
        Assert.Equal("12", imageEffect.Attribute("BlurRadius")?.Value);
        Assert.Equal("0.92", imageEffect.Attribute("Opacity")?.Value);
        Assert.Equal("3", imageEffect.Attribute("ShadowDepth")?.Value);

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var shadowStyle = FindStyle(page, xaml, "HomeForegroundNameShadowStyle");
        var effectSetter = Assert.Single(shadowStyle.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Effect"));
        Assert.Equal(
            "{DynamicResource Effect.Home.ForegroundTextShadow}",
            effectSetter.Attribute("Value")?.Value);
        Assert.Empty(shadowStyle.Descendants().Where(element =>
            element.Name.LocalName == "DropShadowEffect"));
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void OrdinaryThemesPreserveHomeForegroundPalette(string fileName)
    {
        var document = LoadTheme(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expectedColors = new[]
        {
            "{StaticResource Color.F4FFFFFF}",
            "{StaticResource Color.9FFFFFFF}",
            "{StaticResource Color.F0FFFFFF}",
            "{StaticResource Color.FFFFFF}"
        };

        for (var index = 0; index < HomeForegroundKeys.Length; index++)
        {
            var brush = FindResource(document, xaml, "SolidColorBrush", HomeForegroundKeys[index]);
            Assert.Equal(expectedColors[index], brush.Attribute("Color")?.Value);
        }
    }

    private static readonly string[] HomeForegroundKeys =
    [
        "Brush.Home.Name.Foreground",
        "Brush.Home.GameSettings.Foreground",
        "Brush.Home.GameSettings.ForegroundHover",
        "Brush.Home.GameSettings.ForegroundPressed"
    ];

    private static XElement FindResource(
        XDocument document,
        XNamespace xaml,
        string elementName,
        string key) => Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == elementName
            && element.Attribute(xaml + "Key")?.Value == key));

    private static XElement FindStyle(XDocument document, XNamespace xaml, string key) =>
        FindResource(document, xaml, "Style", key);

    private static void AssertStyleSetter(
        XDocument document,
        XNamespace xaml,
        string styleKey,
        string value)
    {
        var style = FindStyle(document, xaml, styleKey);
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Foreground"
            && element.Attribute("Value")?.Value == value);
    }

    private static XDocument LoadTheme(params string[] pathParts) =>
        LoadAppXaml(["Resources", "Themes", .. pathParts]);

    private static XDocument LoadAppXaml(params string[] pathParts) =>
        XDocument.Load(Path.Combine([FindRepositoryRoot().FullName, "Launcher.App", .. pathParts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
