/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ControlStyleHeightContractTests
{
    private const string SharedHeightKey = "LauncherCompactControlHeight";
    private const string SharedHeightReference = "{StaticResource " + SharedHeightKey + "}";

    [Fact]
    public void ButtonsAndReadOnlyFieldsShareCompactControlHeight()
    {
        var shared = LoadAppXaml("Resources", "Themes", "Shared.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var height = Assert.Single(shared.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == SharedHeightKey));
        Assert.Equal("34", height.Value);

        var dialogs = LoadAppXaml("Styles", "ControlStyles.Dialogs.xaml");
        var dialogButtonStyle = FindStyle(dialogs, "DialogButtonStyle", xaml);
        AssertSetter(dialogButtonStyle, "Height", SharedHeightReference);

        var page = LoadAppXaml("Styles", "ControlStyles.Page.xaml");
        var readOnlyFieldStyle = FindStyle(page, "ReadOnlyFieldSurfaceStyle", xaml);
        AssertSetter(readOnlyFieldStyle, "MinHeight", SharedHeightReference);
        Assert.DoesNotContain(readOnlyFieldStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height");
    }

    [Fact]
    public void ReadOnlyFieldUsagesDoNotOverrideAdaptiveHeight()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root.FullName, "Launcher.App");
        var files = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}Controls{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var file in files)
        {
            var document = XDocument.Load(file);
            var fields = document.Descendants()
                .Where(element => element.Attribute("Style")?.Value == "{StaticResource ReadOnlyFieldSurfaceStyle}");
            foreach (var field in fields)
            {
                Assert.Null(field.Attribute("Height"));
                Assert.Null(field.Attribute("MinHeight"));
            }
        }
    }

    private static XElement FindStyle(XDocument document, string key, XNamespace xaml) =>
        Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "Style"
                && element.Attribute(xaml + "Key")?.Value == key));

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == property
            && element.Attribute("Value")?.Value == value);

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
