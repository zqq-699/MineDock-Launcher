/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Launcher.App.Resources;

namespace Launcher.Tests.Resources;

public sealed class StringResourceTests
{
    private static readonly Regex PlaceholderPattern = new(@"\{\d+(?:[^}]*)\}", RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("Strings.en.resx")]
    [InlineData("Strings.zh-Hans.resx")]
    [InlineData("Strings.zh-Hant.resx")]
    [InlineData("Strings.ja-JP.resx")]
    public void LocalizedResourcesMatchDefaultContract(string fileName)
    {
        var defaults = Load("Strings.resx");
        var localized = Load(fileName);

        Assert.Equal(defaults.Keys.Order(), localized.Keys.Order());
        Assert.All(defaults, entry =>
            Assert.Equal(Placeholders(entry.Value), Placeholders(localized[entry.Key])));
    }

    private static IReadOnlyDictionary<string, string> Load(string fileName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        var path = Path.Combine(root.FullName, "Launcher.App", "Resources", fileName);
        return XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string[] Placeholders(string value) => PlaceholderPattern.Matches(value)
        .Select(match => match.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
