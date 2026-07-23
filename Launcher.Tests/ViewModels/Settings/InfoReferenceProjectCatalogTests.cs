/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;
using Launcher.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class InfoReferenceProjectCatalogTests
{
    [Fact]
    public void EmbeddedCatalogContainsValidUniqueProjects()
    {
        var catalog = new EmbeddedInfoReferenceProjectCatalog(
            NullLogger<EmbeddedInfoReferenceProjectCatalog>.Instance);

        var projects = catalog.GetProjects();

        Assert.NotEmpty(projects);
        Assert.Equal(
            projects.Count,
            projects.Select(project => project.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(projects, project =>
        {
            Assert.False(string.IsNullOrWhiteSpace(project.Name));
            Assert.False(string.IsNullOrWhiteSpace(project.CopyrightNotice));
            Assert.False(string.IsNullOrWhiteSpace(project.LicenseText));
            Assert.True(Uri.TryCreate(project.ProjectUrl, UriKind.Absolute, out var uri));
            Assert.Contains(uri!.Scheme, new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps });
        });
    }

    [Fact]
    public void EmbeddedCatalogCoversEveryRuntimePackageReference()
    {
        var catalog = new EmbeddedInfoReferenceProjectCatalog(
            NullLogger<EmbeddedInfoReferenceProjectCatalog>.Instance);
        var catalogNames = catalog.GetProjects()
            .Select(project => project.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = FindRepositoryRoot();
        var runtimePackageNames = new[] { "Launcher.App", "Launcher.Application", "Launcher.Infrastructure" }
            .SelectMany(projectName => LoadPackageReferences(root, projectName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingPackages = runtimePackageNames
            .Where(packageName => !catalogNames.Contains(packageName))
            .ToArray();

        Assert.True(
            missingPackages.Length == 0,
            $"ReferenceProjects.json is missing runtime package projects: {string.Join(", ", missingPackages)}");
    }

    [Fact]
    public void AboutViewDisplaysNameCopyrightAndProjectButton()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "Views",
            "Settings",
            "InfoSettingsView.xaml"));

        Assert.Contains("Text=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CopyrightNotice}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsOptionTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsDescriptionTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.OpenReferenceProjectCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProjectUrl}\"", xaml, StringComparison.Ordinal);
    }

    private static IEnumerable<string> LoadPackageReferences(DirectoryInfo root, string projectName)
    {
        var document = XDocument.Load(Path.Combine(root.FullName, projectName, $"{projectName}.csproj"));
        return document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");

        return root;
    }
}
