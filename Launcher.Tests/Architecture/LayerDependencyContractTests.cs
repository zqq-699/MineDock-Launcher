/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Architecture;

public sealed class LayerDependencyContractTests
{
    [Fact]
    public void ProjectReferencesFollowLayerDirection()
    {
        var root = FindRepositoryRoot();

        AssertProjectReferences(root, "Launcher.Domain");
        AssertProjectReferences(root, "Launcher.Application", "Launcher.Domain");
        AssertProjectReferences(root, "Launcher.Infrastructure", "Launcher.Application", "Launcher.Domain");
        AssertProjectReferences(root, "Launcher.App", "Launcher.Application", "Launcher.Domain", "Launcher.Infrastructure");
    }

    [Fact]
    public void SourceDoesNotRestoreLauncherCoreOrReverseLayerDependencies()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = EnumerateSourceFiles(root)
            .Where(file => new[] { "Launcher.App", "Launcher.Application", "Launcher.Domain", "Launcher.Infrastructure" }
                .Any(project => IsUnder(file, root, project)))
            .ToArray();

        Assert.DoesNotContain(sourceFiles, file => File.ReadAllText(file).Contains("using Launcher.Core", StringComparison.Ordinal));
        Assert.DoesNotContain(
            sourceFiles.Where(file => IsUnder(file, root, "Launcher.Infrastructure")),
            file => HasUsing(file, "Launcher.App"));
        Assert.DoesNotContain(
            sourceFiles.Where(file => IsUnder(file, root, "Launcher.Application")),
            file => HasUsing(file, "Launcher.Infrastructure") || HasUsing(file, "Launcher.App"));
        Assert.DoesNotContain(
            sourceFiles.Where(file => IsUnder(file, root, "Launcher.Domain")),
            file => HasUsing(file, "Launcher.Application")
                || HasUsing(file, "Launcher.Infrastructure")
                || HasUsing(file, "Launcher.App"));
    }

    [Fact]
    public void AppReferencesInfrastructureOnlyFromCompositionRoot()
    {
        var root = FindRepositoryRoot();
        var offendingFiles = EnumerateSourceFiles(root)
            .Where(file => IsUnder(file, root, "Launcher.App"))
            .Where(file => !string.Equals(Path.GetFileName(file), "App.xaml.cs", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("using Launcher.Infrastructure", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ViewModelsDoNotOwnFileSystemWatchers()
    {
        var root = FindRepositoryRoot();
        var offendingFiles = EnumerateSourceFiles(root)
            .Where(file => IsUnder(file, root, "Launcher.App"))
            .Where(file => file.Contains(
                $"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("FileSystemWatcher", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ViewModelsDoNotReadOrWriteFileSystemDirectly()
    {
        var root = FindRepositoryRoot();
        var viewModelsRoot = Path.Combine(
            root.FullName,
            "Launcher.App",
            "ViewModels") + Path.DirectorySeparatorChar;
        var offendingFiles = EnumerateSourceFiles(root)
            .Where(file => file.StartsWith(viewModelsRoot, StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("File.", StringComparison.Ordinal)
                    || source.Contains("Directory.", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ShellKeepsStateMonitoringAndBlockingShutdownOutOfUiBoundaries()
    {
        var root = FindRepositoryRoot();
        var mainWindowSource = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml.cs"));
        var appSource = File.ReadAllText(Path.Combine(root.FullName, "Launcher.App", "App.xaml.cs"));

        Assert.DoesNotContain("ILauncherStateMonitor", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSystemWatcher", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherCultureIsAppliedBeforeWpfStartsLoadingResources()
    {
        var root = FindRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(root.FullName, "Launcher.App", "App.xaml.cs"));
        var constructorIndex = appSource.IndexOf("public App()", StringComparison.Ordinal);
        var startupIndex = appSource.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        var bootstrapLanguageIndex = appSource.IndexOf(
            "LoadLauncherBootstrapPreferences()",
            StringComparison.Ordinal);

        Assert.True(constructorIndex >= 0);
        Assert.True(bootstrapLanguageIndex > constructorIndex);
        Assert.True(startupIndex > bootstrapLanguageIndex);
    }

    [Fact]
    public void ProductionCodeDoesNotSynchronouslyBlockOnTasksOrUseAsyncVoidViewModels()
    {
        var root = FindRepositoryRoot();
        var productionFiles = EnumerateSourceFiles(root)
            .Where(file => new[] { "Launcher.App", "Launcher.Application", "Launcher.Domain", "Launcher.Infrastructure" }
                .Any(project => IsUnder(file, root, project)))
            .ToArray();
        var blockingFiles = productionFiles
            .Where(file => File.ReadAllText(file).Contains("GetAwaiter().GetResult()", StringComparison.Ordinal))
            .ToArray();
        var asyncVoidViewModels = productionFiles
            .Where(file => file.Contains(
                $"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("async void", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(blockingFiles);
        Assert.Empty(asyncVoidViewModels);
    }

    [Fact]
    public void OnlineResourcePageDoesNotReabsorbChildRequestState()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "ViewModels",
            "Resources",
            "ResourcesModPageViewModel.cs"));

        Assert.DoesNotContain("CancellationTokenSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableCollection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchQuery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAvailableVersionsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResourcesProjectListViewModel ProjectList", source, StringComparison.Ordinal);
        Assert.Contains("ResourcesProjectVersionsViewModel Versions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountAppearanceParentOnlyComposesAccountSubmodules()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "ViewModels",
            "Account",
            "AccountAppearanceViewModel.cs"));

        Assert.DoesNotContain("[RelayCommand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadSkinAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetActiveCapeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshAccountProfileAsync", source, StringComparison.Ordinal);
        Assert.Contains("AccountProfileViewModel Profile", source, StringComparison.Ordinal);
        Assert.Contains("AccountSkinLibraryViewModel SkinLibrary", source, StringComparison.Ordinal);
        Assert.Contains("AccountCapeViewModel Cape", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountProfileRefreshKeepsUiContinuationContext()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "ViewModels",
            "Account",
            "AccountProfileViewModel.cs"));

        Assert.DoesNotContain("ConfigureAwait(false)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GameSettingsPageDoesNotReabsorbInstanceListOrDialogState()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root.FullName,
            "Launcher.App",
            "ViewModels",
            "GameSettings",
            "GameSettingsPageViewModel.cs"));

        Assert.DoesNotContain("IGameInstanceService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableCollection<GameSettingsInstanceItem>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDeleteInstanceDialogOpen", source, StringComparison.Ordinal);
        Assert.Contains("GameSettingsInstanceListViewModel InstanceList", source, StringComparison.Ordinal);
        Assert.Contains("GameSettingsDialogsViewModel Dialogs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimatedComboBoxPopupCannotClearOwnerSelectionDuringTemplateTeardown()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            root.FullName,
            "Launcher.App",
            "Styles",
            "ControlStyles.Inputs.xaml"));
        var popupList = Assert.Single(document
            .Descendants()
            .Where(element => element.Name.LocalName == "ListBox")
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "PART_DropDownList")));

        var selectedItemBinding = popupList.Attributes()
            .Single(attribute => attribute.Name.LocalName == "SelectedItem")
            .Value;

        Assert.Contains("Mode=OneWay", selectedItemBinding, StringComparison.Ordinal);
        Assert.DoesNotContain(
            popupList.Attributes(),
            attribute => attribute.Name.LocalName == "SelectedValue");
    }

    private static void AssertProjectReferences(DirectoryInfo root, string project, params string[] expectedReferences)
    {
        var projectPath = Path.Combine(root.FullName, project, $"{project}.csproj");
        var document = XDocument.Load(projectPath);
        var references = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), references);
    }

    private static IEnumerable<string> EnumerateSourceFiles(DirectoryInfo root)
    {
        return Directory.EnumerateFiles(root.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnder(string file, DirectoryInfo root, string project)
    {
        var projectRoot = Path.Combine(root.FullName, project) + Path.DirectorySeparatorChar;
        return file.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsing(string file, string namespacePrefix)
    {
        return File.ReadLines(file).Any(line =>
            line.StartsWith($"using {namespacePrefix}.", StringComparison.Ordinal)
            || string.Equals(line, $"using {namespacePrefix};", StringComparison.Ordinal));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
