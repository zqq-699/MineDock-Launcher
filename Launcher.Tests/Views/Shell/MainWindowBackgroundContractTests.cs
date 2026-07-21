/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Shell;

public sealed class MainWindowBackgroundContractTests
{
    [Fact]
    public void WindowFrameUsesTheBackgroundModeSpecificBorderBrush()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        var windowFrame = Assert.Single(document.Root!.Elements().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("BorderBrush")?.Value ==
                "{DynamicResource Brush.Surface.WindowBorder}"));

        Assert.Equal(
            "{DynamicResource Brush.Surface.WindowBorder}",
            windowFrame.Attribute("BorderBrush")?.Value);
        Assert.Equal(
            "{DynamicResource Thickness.Surface.WindowBorder}",
            windowFrame.Attribute("BorderThickness")?.Value);
    }

    [Fact]
    public void BackgroundImageFillsWholeWindowAndOnlyHidesPageBackdropWhenActive()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));

        var imageBrush = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ImageBrush"
            && element.Attribute("ImageSource")?.Value == "{Binding LauncherBackground.ImageSource}"));
        Assert.Equal("UniformToFill", imageBrush.Attribute("Stretch")?.Value);

        var pageBackdrop = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Background")?.Value == "{DynamicResource Brush.Page.Background}"));
        var activeTrigger = Assert.Single(pageBackdrop.Descendants().Where(element =>
            element.Name.LocalName == "DataTrigger"
            && element.Attribute("Binding")?.Value == "{Binding LauncherBackground.IsActive}"
            && element.Attribute("Value")?.Value == "True"));
        var opacitySetter = Assert.Single(activeTrigger.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Opacity"));
        Assert.Equal("0", opacitySetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void ActiveImageBackgroundIsDimmedOutsideHomePage()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var dimLayer = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "LauncherBackgroundImageDimLayer"));
        Assert.Equal(
            "{DynamicResource Brush.LauncherBackground.Image.DimOverlay}",
            dimLayer.Attribute("Background")?.Value);
        Assert.Equal("False", dimLayer.Attribute("IsHitTestVisible")?.Value);

        var activeTrigger = Assert.Single(dimLayer.Descendants().Where(element =>
            element.Name.LocalName == "DataTrigger"
            && element.Attribute("Binding")?.Value == "{Binding LauncherBackground.IsActive}"
            && element.Attribute("Value")?.Value == "True"));
        var dimSetter = Assert.Single(activeTrigger.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Opacity"));
        Assert.Equal(
            "{DynamicResource Opacity.LauncherBackground.Image.NonHomeDim}",
            dimSetter.Attribute("Value")?.Value);

        var homeTrigger = Assert.Single(dimLayer.Descendants().Where(element =>
            element.Name.LocalName == "DataTrigger"
            && element.Attribute("Binding")?.Value == "{Binding CurrentPage}"
            && element.Attribute("Value")?.Value == "Home"));
        var homeOpacitySetter = Assert.Single(homeTrigger.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Opacity"));
        Assert.Equal("0", homeOpacitySetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void ImageAndDimLayersFormAStandaloneBackdropSourceBehindWindowContent()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var source = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute(xaml + "Name")?.Value == "LauncherBackgroundVisualSource"));
        var dimLayer = Assert.Single(source.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "LauncherBackgroundImageDimLayer"));
        Assert.NotNull(source.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "ImageBrush"
            && element.Attribute("ImageSource")?.Value == "{Binding LauncherBackground.ImageSource}"));
        Assert.Equal("False", source.Attribute("IsHitTestVisible")?.Value);
        Assert.Same(source, dimLayer.Parent);

        var windowContent = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute(xaml + "Name")?.Value == "WindowContentLayer"));
        Assert.Same(source.Parent, windowContent.Parent);
        var siblingLayers = source.Parent!.Elements().ToArray();
        Assert.True(Array.IndexOf(siblingLayers, source) < Array.IndexOf(siblingLayers, windowContent));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
