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

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja-JP")]
    public void SupportedCultureResolvesResources(string cultureName)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            Assert.False(string.IsNullOrWhiteSpace(Strings.Page_Settings));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("Strings.resx", "另存为")]
    [InlineData("Strings.zh-Hans.resx", "另存为")]
    [InlineData("Strings.zh-Hant.resx", "另存新檔")]
    [InlineData("Strings.en.resx", "Save As")]
    [InlineData("Strings.ja-JP.resx", "名前を付けて保存")]
    public void ResourceLocalInstallTargetsUseSaveAsCopy(string fileName, string expected)
    {
        var resources = Load(fileName);
        var keys = new[]
        {
            nameof(Strings.Resources_ModInstallTargetLocal),
            nameof(Strings.Resources_ResourcePackInstallTargetLocal),
            nameof(Strings.Resources_ShaderPackInstallTargetLocal),
            nameof(Strings.Resources_WorldInstallTargetLocal),
            nameof(Strings.Resources_ModpackInstallTargetLocal)
        };

        Assert.All(keys, key => Assert.Equal(expected, resources[key]));
    }

    [Theory]
    [InlineData("Strings.resx", "大厅")]
    [InlineData("Strings.zh-Hans.resx", "大厅")]
    [InlineData("Strings.zh-Hant.resx", "大廳")]
    [InlineData("Strings.en.resx", "lobby")]
    [InlineData("Strings.ja-JP.resx", "ロビー")]
    public void MultiplayerCopyDoesNotUseLegacyLobbyTerm(string fileName, string legacyTerm)
    {
        var multiplayerResources = Load(fileName)
            .Where(entry => entry.Key.StartsWith("Multiplayer_", StringComparison.Ordinal)
                || entry.Key.StartsWith("Dialog_Multiplayer", StringComparison.Ordinal));

        Assert.DoesNotContain(multiplayerResources, entry =>
            entry.Value.Contains(legacyTerm, StringComparison.OrdinalIgnoreCase));
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
