/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.ExceptionServices;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Views.Resources;

public sealed class ResourcesModPageViewContractTests
{
    [Theory]
    [InlineData(ResourceProjectKind.Mod, true)]
    [InlineData(ResourceProjectKind.ResourcePack, false)]
    [InlineData(ResourceProjectKind.ShaderPack, false)]
    [InlineData(ResourceProjectKind.World, false)]
    [InlineData(ResourceProjectKind.Modpack, true)]
    public void LoaderDetailsAreOnlyAvailableForModsAndModpacks(ResourceProjectKind kind, bool expected)
    {
        var project = new ResourceProject
        {
            Kind = kind
        };

        var item = new ResourcesModProjectItemViewModel(project);

        Assert.Equal(expected, item.ShowsLoaders);
    }

    [Fact]
    public void ProjectTitleTagsExposeAllMatchingUnifiedTitles()
    {
        var project = new ResourceProject
        {
            Kind = ResourceProjectKind.Mod,
            Categories =
            [
                ResourceProjectCategory.Optimization,
                ResourceProjectCategory.Decoration,
                ResourceProjectCategory.Utility,
                ResourceProjectCategory.Magic,
                ResourceProjectCategory.Optimization,
                ResourceProjectCategory.Audio
            ]
        };
        ResourcesOnlineProjectTypeOption[] typeOptions =
        [
            new("optimization", "Optimization", ResourceProjectCategory.Optimization),
            new("decoration", "Decoration", ResourceProjectCategory.Decoration),
            new("utility", "Utility", ResourceProjectCategory.Utility),
            new("magic", "Magic", ResourceProjectCategory.Magic)
        ];

        var item = new ResourcesModProjectItemViewModel(project, typeOptions: typeOptions);

        Assert.True(item.HasTitleTags);
        Assert.Equal(["Optimization", "Decoration", "Utility", "Magic"], item.TitleTags);
        Assert.Equal("Optimization, Decoration, Utility, Magic", item.TitleTagsText);
    }

    [Fact]
    public void ProjectTitleTagsAreEmptyWithoutMatchingUnifiedOptions()
    {
        var project = new ResourceProject
        {
            Categories = [ResourceProjectCategory.Audio]
        };

        var item = new ResourcesModProjectItemViewModel(
            project,
            typeOptions: [new("optimization", "Optimization", ResourceProjectCategory.Optimization)]);

        Assert.False(item.HasTitleTags);
        Assert.Empty(item.TitleTags);
    }

    [Fact]
    public void ProjectTitleTagsPreserveSourceOrder()
    {
        var project = new ResourceProject
        {
            Categories =
            [
                ResourceProjectCategory.Optimization,
                ResourceProjectCategory.Decoration,
                ResourceProjectCategory.Utility
            ]
        };
        ResourcesOnlineProjectTypeOption[] typeOptions =
        [
            new("optimization", "Optimization", ResourceProjectCategory.Optimization),
            new("decoration", "Decoration", ResourceProjectCategory.Decoration),
            new("utility", "Utility", ResourceProjectCategory.Utility)
        ];

        var item = new ResourcesModProjectItemViewModel(project, typeOptions: typeOptions);

        Assert.Equal(["Optimization", "Decoration", "Utility"], item.TitleTags);
    }

    [Fact]
    public void LoaderVisibilityBindingBelongsToLoaderDetailRow()
    {
        var document = LoadView();

        var loaderRow = FindDetailRow(document, "Resources_ModDetailsLoaderLabel");
        var versionRow = FindDetailRow(document, "Resources_ModDetailsVersionLabel");

        Assert.Contains("ShowsLoaders", loaderRow.Attribute("Visibility")?.Value, StringComparison.Ordinal);
        Assert.Null(versionRow.Attribute("Visibility"));
    }

    [Fact]
    public void DetailsTagsRowDisplaysAllProjectTagsAsCommaSeparatedText()
    {
        var row = FindDetailRow(LoadView(), "Resources_ModDetailsTagsLabel");
        var tagText = Assert.Single(row
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains("TitleTagsText", StringComparison.Ordinal) == true));

        Assert.Contains("HasTitleTags", row.Attribute("Visibility")?.Value, StringComparison.Ordinal);
        Assert.Contains("ResourcesModDetailValueTextStyle", tagText.Attribute("Style")?.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(row.Descendants(), element => element.Name.LocalName == "ItemsControl");
    }

    [Fact]
    public void SourceDetailUsesProjectPageHyperlink()
    {
        var row = FindDetailRow(LoadView(), "Resources_ModDetailsSourceLabel");
        var hyperlink = Assert.Single(row.Descendants()
            .Where(element => element.Name.LocalName == "Hyperlink"));
        var sourceText = Assert.Single(hyperlink.Descendants()
            .Where(element => element.Name.LocalName == "Run"));

        Assert.Contains("Parent.OpenProjectPageCommand", hyperlink.Attribute("Command")?.Value, StringComparison.Ordinal);
        Assert.Equal("{Binding}", hyperlink.Attribute("CommandParameter")?.Value);
        Assert.Contains("ResourcesModDetailHyperlinkStyle", hyperlink.Attribute("Style")?.Value, StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", sourceText.Attribute("Text")?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectPageCommandOpensHttpProjectUrl()
    {
        const string projectUrl = "https://modrinth.com/mod/sodium";
        var externalLinks = new RecordingExternalLinkService();
        var parent = new ResourcesPageViewModel(externalLinkService: externalLinks);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            ProjectUrl = projectUrl
        });

        Assert.True(parent.OpenProjectPageCommand.CanExecute(project));

        parent.OpenProjectPageCommand.Execute(project);

        Assert.Equal(projectUrl, externalLinks.LastUrl);
    }

    [Fact]
    public void ProjectPageCommandRejectsNonHttpUrl()
    {
        var externalLinks = new RecordingExternalLinkService();
        var parent = new ResourcesPageViewModel(externalLinkService: externalLinks);
        var project = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            ProjectUrl = "file:///C:/Windows/System32/calc.exe"
        });

        Assert.False(parent.OpenProjectPageCommand.CanExecute(project));
    }

    [Fact]
    public async Task RecognizedLocalResourceOpensMatchingResourceSectionDetails()
    {
        var project = new ResourceProject
        {
            Kind = ResourceProjectKind.ShaderPack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "complementary-reimagined",
            Title = "Complementary Reimagined"
        };
        var parent = new ResourcesPageViewModel(resourceCatalogService: new ProjectCatalogService(project));

        var opened = await parent.OpenProjectDetailsAsync(new ResourceProjectReference(
            project.Kind,
            project.Source,
            project.ProjectId));

        Assert.True(opened);
        Assert.Equal("shader_packs", parent.SelectedSection?.Id);
        Assert.Equal(ResourcesModPageStep.ProjectDetails, parent.ShaderPacksPage.CurrentStep);
        Assert.Same(project, parent.ShaderPacksPage.Details.CurrentProject?.Project);
    }

    [Fact]
    public void ProjectListTemplateUsesAdaptiveTitleTags()
    {
        var template = Assert.Single(LoadView()
            .Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate")
            .Where(element => element.Attribute("DataType")?.Value.Contains(
                nameof(ResourcesModProjectItemViewModel),
                StringComparison.Ordinal) == true));
        var tagList = Assert.Single(template
            .Descendants()
            .Where(element => element.Name.LocalName == nameof(AdaptiveTagList))
            .Where(element => element.Attribute("ItemsSource")?.Value.Contains("TitleTags", StringComparison.Ordinal) == true));

        Assert.Contains("HasTitleTags", tagList.Attribute("Visibility")?.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200, 4)]
    [InlineData(105, 2)]
    [InlineData(70, 1)]
    public void AdaptiveTitleTagsUseAvailableWidth(double availableWidth, int expectedVisibleCount)
    {
        var visibleCount = AdaptiveTagList.CalculateVisibleItemCount(
            [30, 30, 30, 30],
            availableWidth,
            _ => 40);

        Assert.Equal(expectedVisibleCount, visibleCount);
    }

    [Fact]
    public void AdaptiveTitleTagsCanArrangeWhenAllTagsFit()
    {
        RunOnStaThread(() =>
        {
            var tags = new AdaptiveTagList
            {
                ItemsSource = new[] { "Optimization", "Decoration" }
            };

            tags.Measure(new Size(300, 24));
            tags.Arrange(new Rect(0, 0, 300, 24));

            Assert.True(tags.IsArrangeValid);
        });
    }

    [Fact]
    public void AdaptiveTitleTagsCanArrangeWhenTagsOverflow()
    {
        RunOnStaThread(() =>
        {
            var tags = new AdaptiveTagList
            {
                ItemsSource = new[] { "Optimization", "Decoration", "Utility", "Magic" }
            };

            tags.Measure(new Size(90, 24));
            tags.Arrange(new Rect(0, 0, 90, 24));

            Assert.True(tags.IsArrangeValid);
        });
    }

    [Fact]
    public void AdaptiveTitleTagsDoNotOverlapTrailingDownloadText()
    {
        RunOnStaThread(() =>
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleRow = new DockPanel { LastChildFill = false, ClipToBounds = true };
            var title = new TextBlock
            {
                Text = "Lucky Oneblock - Lucky Blocks - Multiplayer - [1.21.11]",
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            var tags = new AdaptiveTagList
            {
                Margin = new Thickness(8, 0, 0, 0),
                ItemsSource = new[] { "Adventure", "Game Map", "Survival", "Parkour" }
            };
            DockPanel.SetDock(title, Dock.Left);
            DockPanel.SetDock(tags, Dock.Left);
            titleRow.Children.Add(title);
            titleRow.Children.Add(tags);
            var downloads = new TextBlock { Text = "237.87万 下载" };
            Grid.SetColumn(downloads, 1);
            row.Children.Add(titleRow);
            row.Children.Add(downloads);

            row.Measure(new Size(600, 24));
            row.Arrange(new Rect(0, 0, 600, 24));
            var tagsRight = tags.TranslatePoint(new Point(tags.ActualWidth, 0), row).X;
            var downloadsLeft = downloads.TranslatePoint(new Point(0, 0), row).X;

            Assert.True(tagsRight <= downloadsLeft);
        });
    }

    [Fact]
    public void ListItemTitleTrailingContentUsesRemainingWidthAfterTitle()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Controls",
            "Lists",
            "ListPageItemButton.xaml"));
        var titleRow = Assert.Single(document
            .Descendants()
            .Where(element => element.Name.LocalName == "DockPanel")
            .Where(element => element.Elements().Any(child => child.Name.LocalName == "TextBlock"
                && child.Attribute("Text")?.Value.Contains("Title, ElementName=Root", StringComparison.Ordinal) == true)));
        var children = titleRow.Elements().ToList();

        Assert.Equal("False", titleRow.Attribute("LastChildFill")?.Value);
        Assert.Equal("True", titleRow.Attribute("ClipToBounds")?.Value);
        Assert.Equal("TextBlock", children[0].Name.LocalName);
        Assert.Equal("ContentPresenter", children[1].Name.LocalName);
        Assert.Contains("TitleTrailingContent", children[1].Attribute("Content")?.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ProjectList.LoadingMessage", "0,132,12,0")]
    [InlineData("ProjectList.EmptyMessage", "0,132,12,0")]
    [InlineData("ProjectList.LoadErrorMessage", "0,132,12,0")]
    [InlineData("Versions.LoadingMessage", "0,172,12,64")]
    [InlineData("Versions.EmptyMessage", "0,172,12,64")]
    [InlineData("Versions.LoadErrorMessage", "0,172,12,64")]
    public void ListStateMessagesAreCenteredWithinTheListViewport(string bindingPath, string expectedMargin)
    {
        var message = Assert.Single(LoadView()
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains(bindingPath, StringComparison.Ordinal) == true));

        Assert.Equal(expectedMargin, message.Attribute("Margin")?.Value);
    }

    [Theory]
    [InlineData("ResourcesModListBox", "132", "0,132,12,64")]
    [InlineData("ResourcesModVersionListBox", "172", "0,172,0,64")]
    public void ListOffsetsMatchTheirVisibleHeaderRows(string listName, string expectedOffset, string expectedPanelMargin)
    {
        var list = Assert.Single(LoadView()
            .Descendants()
            .Where(element => element.Name.LocalName == "ListBox")
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == listName)));

        var offset = Assert.Single(list.Attributes()
            .Where(attribute => attribute.Name.LocalName.EndsWith(".ContentTopOffset", StringComparison.Ordinal)));
        var panel = Assert.Single(list.Descendants()
            .Where(element => element.Name.LocalName == "VirtualizingStackPanel"));

        Assert.Equal(expectedOffset, offset.Value);
        Assert.Equal(expectedPanelMargin, panel.Attribute("Margin")?.Value);
    }

    private static XElement FindDetailRow(XDocument document, string labelKey)
    {
        var label = Assert.Single(document
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => element.Attribute("Text")?.Value.Contains(labelKey, StringComparison.Ordinal) == true));

        return label.Ancestors().First(element => element.Name.LocalName == "Grid");
    }

    private static XDocument LoadView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot().FullName,
        "Launcher.App",
        "Views",
        "Resources",
        "ResourcesModPageView.xaml"));

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }

    private sealed class RecordingExternalLinkService : IExternalLinkService
    {
        public string? LastUrl { get; private set; }

        public bool TryOpen(string url)
        {
            LastUrl = url;
            return true;
        }
    }

    private sealed class ProjectCatalogService(ResourceProject project) : IResourceCatalogService
    {
        public Task<ResourceProject?> GetProjectAsync(
            ResourceProjectReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResourceProject?>(project);

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceCatalogSearchResult());

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceProjectVersionsResult());

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
