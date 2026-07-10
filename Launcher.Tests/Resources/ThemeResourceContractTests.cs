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

    private static HashSet<string> LoadKeys(string fileName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        var document = XDocument.Load(Path.Combine(root.FullName, "Launcher.App", "Resources", "Themes", fileName));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
