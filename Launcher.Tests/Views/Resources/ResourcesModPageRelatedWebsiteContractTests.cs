/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Resources;

public sealed class ResourcesModPageRelatedWebsiteContractTests
{
    [Fact]
    public void RelatedWebsiteRowFollowsSourceAndUsesConditionalMcresHyperlink()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Resources",
            "ResourcesModPageView.xaml"));
        var sourceLabel = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{x:Static res:Strings.Resources_ModDetailsSourceLabel}"));
        var relatedLabel = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{x:Static res:Strings.Resources_ModDetailsRelatedWebsitesLabel}"));
        var sourceRow = sourceLabel.Parent!;
        var relatedRow = relatedLabel.Parent!;
        var rows = sourceRow.Parent!.Elements().ToList();

        Assert.Equal(rows.IndexOf(sourceRow) + 1, rows.IndexOf(relatedRow));
        Assert.Contains("Details.HasRelatedWebsite", relatedRow.Attribute("Visibility")?.Value, StringComparison.Ordinal);
        var hyperlink = Assert.Single(relatedRow.Descendants().Where(element => element.Name.LocalName == "Hyperlink"));
        Assert.Contains("OpenRelatedWebsiteCommand", hyperlink.Attribute("Command")?.Value, StringComparison.Ordinal);
        Assert.Contains("Details.RelatedWebsite", hyperlink.Attribute("CommandParameter")?.Value, StringComparison.Ordinal);
        var run = Assert.Single(hyperlink.Descendants().Where(element => element.Name.LocalName == "Run"));
        Assert.Contains("RelatedWebsite.Name", run.Attribute("Text")?.Value, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
