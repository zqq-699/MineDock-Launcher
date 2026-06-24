using Launcher.App.Resources;

namespace Launcher.Tests.Resources;

public sealed class ResourcesPageViewModelTests
{
    [Fact]
    public void ResourcesPageShowsExpectedSectionsAndSelectsModByDefault()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal(
            [
                Strings.Resources_SectionMods,
                Strings.Resources_SectionResourcePacks,
                Strings.Resources_SectionShaderPacks,
                Strings.Resources_SectionWorlds,
                Strings.Resources_SectionModpacks
            ],
            viewModel.Sections.Select(section => section.Title));
        Assert.Same(viewModel.Sections[0], viewModel.SelectedSection);
        Assert.True(viewModel.Sections[0].IsSelected);
        Assert.All(viewModel.Sections.Skip(1), section => Assert.False(section.IsSelected));
        Assert.True(viewModel.IsModsSection);
        Assert.Same(viewModel.ModPage, viewModel.CurrentSectionViewModel);
        Assert.Equal(string.Empty, viewModel.ModPage.SearchQuery);
    }

    [Fact]
    public void SelectSectionCommandUpdatesSelectionState()
    {
        var viewModel = new ResourcesPageViewModel();
        var targetSection = viewModel.Sections.Single(section => section.Id == "worlds");

        viewModel.SelectSectionCommand.Execute(targetSection);

        Assert.Same(targetSection, viewModel.SelectedSection);
        Assert.False(viewModel.Sections[0].IsSelected);
        Assert.True(targetSection.IsSelected);
        Assert.Equal(targetSection.Title, viewModel.PageTitle);
        Assert.False(viewModel.IsModsSection);
        Assert.Same(viewModel.WorldsPage, viewModel.CurrentSectionViewModel);
    }

    [Fact]
    public void ModFiltersUseExpectedDefaults()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal([Strings.Resources_ModFilterAllVersions], viewModel.ModPage.VersionOptions.Select(option => option.Title));
        Assert.Equal([Strings.Resources_ModFilterAllLoaders], viewModel.ModPage.LoaderOptions.Select(option => option.Title));
        Assert.Equal([Strings.Resources_ModFilterAllSources], viewModel.ModPage.SourceOptions.Select(option => option.Title));
        Assert.Equal([Strings.Resources_ModFilterAllTypes], viewModel.ModPage.TypeOptions.Select(option => option.Title));
        Assert.Same(viewModel.ModPage.VersionOptions[0], viewModel.ModPage.SelectedVersionOption);
        Assert.Same(viewModel.ModPage.LoaderOptions[0], viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(viewModel.ModPage.SourceOptions[0], viewModel.ModPage.SelectedSourceOption);
        Assert.Same(viewModel.ModPage.TypeOptions[0], viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public void ModFilterSelectionCanBeChanged()
    {
        var viewModel = new ResourcesPageViewModel();
        var versionOption = new ResourcesFilterOptionItem { Id = "1.21", Title = "1.21" };
        var loaderOption = new ResourcesFilterOptionItem { Id = "fabric", Title = "Fabric" };
        var sourceOption = new ResourcesFilterOptionItem { Id = "modrinth", Title = "Modrinth" };
        var typeOption = new ResourcesFilterOptionItem { Id = "library", Title = "Library" };

        viewModel.ModPage.VersionOptions.Add(versionOption);
        viewModel.ModPage.LoaderOptions.Add(loaderOption);
        viewModel.ModPage.SourceOptions.Add(sourceOption);
        viewModel.ModPage.TypeOptions.Add(typeOption);

        viewModel.ModPage.SelectedVersionOption = versionOption;
        viewModel.ModPage.SelectedLoaderOption = loaderOption;
        viewModel.ModPage.SelectedSourceOption = sourceOption;
        viewModel.ModPage.SelectedTypeOption = typeOption;

        Assert.Same(versionOption, viewModel.ModPage.SelectedVersionOption);
        Assert.Same(loaderOption, viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(sourceOption, viewModel.ModPage.SelectedSourceOption);
        Assert.Same(typeOption, viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public void SelectSectionCommandMapsAllSectionsToChildViewModels()
    {
        var viewModel = new ResourcesPageViewModel();

        var expectedMappings = new Dictionary<string, ResourcesSectionViewModelBase>
        {
            ["mods"] = viewModel.ModPage,
            ["resource_packs"] = viewModel.ResourcePacksPage,
            ["shader_packs"] = viewModel.ShaderPacksPage,
            ["worlds"] = viewModel.WorldsPage,
            ["modpacks"] = viewModel.ModpacksPage
        };

        foreach (var section in viewModel.Sections)
        {
            viewModel.SelectSectionCommand.Execute(section);

            Assert.Same(expectedMappings[section.Id], viewModel.CurrentSectionViewModel);
            Assert.Equal(section.Id == "mods", viewModel.IsModsSection);
        }
    }
}
