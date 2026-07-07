using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.App.ViewModels.Resources;
using Launcher.App.Views.GameSettings;
using Launcher.App.Views.Account.Dialogs;
using Launcher.App.Views.Resources;
using Launcher.Application.Services;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Resources;

[Collection(Launcher.Tests.WpfTestCollection.Name)]
public sealed class ResourceDictionaryTests
{
    [Fact]
    public void ControlStylesResourceDictionaryLoadsAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current;
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MineDock%20Launcher;component/Styles/ControlStyles.xaml",
                        UriKind.Absolute)
                };

                Assert.NotNull(dictionary["NavigationMenuButtonStyle"]);
                Assert.NotNull(dictionary["MainContentScrollViewerStyle"]);
                Assert.NotNull(dictionary["DownloadVersionListBoxStyle"]);
                Assert.NotNull(dictionary["ListPageItemButtonEntranceStyle"]);
                Assert.NotNull(dictionary["LauncherDialogButtonStyle"]);
                Assert.NotNull(dictionary["LauncherComboBoxStyle"]);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ThemeResourceDictionariesLoadAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = GetOrCreateApplication();
                var shared = LoadDictionary("Resources/Themes/Shared.xaml");
                var dark = LoadDictionary("Resources/Themes/Dark.xaml");
                var light = LoadDictionary("Resources/Themes/Light.xaml");
                var purpleAccent = LoadDictionary("Resources/Themes/Accents/Purple.xaml");

                Assert.NotNull(shared["LauncherFontFamily"]);
                Assert.NotNull(dark["Brush.Text.Primary"]);
                Assert.NotNull(light["Brush.Text.Primary"]);
                Assert.NotNull(dark["Brush.Icon.Primary"]);
                Assert.NotNull(light["Brush.Icon.Primary"]);
                Assert.Equal("#804A4A4A", ((SolidColorBrush)dark["Brush.SecondaryMenu.Panel"]).Color.ToString());
                Assert.Equal("#CC252525", ((SolidColorBrush)dark["Brush.Surface.Popup"]).Color.ToString());
                Assert.Equal("#FF181818", ((SolidColorBrush)dark["Brush.Page.Background"]).Color.ToString());
                Assert.Equal(0.85d, Assert.IsType<double>(dark["Opacity.Page.Background"]), 3);
                Assert.Equal("#80FFFFFF", ((SolidColorBrush)light["Brush.Surface.Popup"]).Color.ToString());
                Assert.Equal("#80FFFFFF", ((SolidColorBrush)light["Brush.SecondaryMenu.Panel"]).Color.ToString());
                Assert.Equal("#24000000", ((SolidColorBrush)light["Brush.SecondaryMenu.Border"]).Color.ToString());
                Assert.Equal("#52000000", ((Color)light["Color.SecondaryMenu.Shadow"]).ToString());
                Assert.Equal("#FFECEEF1", ((SolidColorBrush)light["Brush.Page.Background"]).Color.ToString());
                Assert.Equal(0.85d, Assert.IsType<double>(light["Opacity.Page.Background"]), 3);
                Assert.Equal("#80FFFFFF", ((SolidColorBrush)light["Brush.Card.Surface"]).Color.ToString());
                Assert.Equal("#08000000", ((SolidColorBrush)light["Brush.Card.Border"]).Color.ToString());
                Assert.Equal("#12000000", ((SolidColorBrush)light["Brush.Input.TextBox.Background"]).Color.ToString());
                Assert.Equal("#12000000", ((SolidColorBrush)light["Brush.Input.ComboBox.Background"]).Color.ToString());
                Assert.Equal("#12000000", ((SolidColorBrush)light["Brush.Button.Secondary.Background"]).Color.ToString());
                Assert.Equal("#12000000", ((SolidColorBrush)light["Brush.Field.ReadOnly.Surface"]).Color.ToString());
                Assert.Equal("#20000000", ((SolidColorBrush)light["Brush.List.Item.Selected"]).Color.ToString());
                Assert.Equal("#FF8B5CF6", ((SolidColorBrush)purpleAccent["LauncherAccentBrush"]).Color.ToString());
                Assert.Equal("#888B5CF6", ((SolidColorBrush)purpleAccent["Brush.List.Item.SelectedBorder"]).Color.ToString());
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ResourcesModUnknownInstanceVersionDialogResourcesAreDeclared()
    {
        var viewModel = new ResourcesPageViewModel();

        viewModel.ModPage.IsUnknownInstanceVersionDialogOpen = true;
        viewModel.ModPage.CloseUnknownInstanceVersionDialogCommand.Execute(null);

        Assert.False(viewModel.ModPage.IsUnknownInstanceVersionDialogOpen);
        Assert.NotEqual(
            nameof(Strings.Resources_ModUnknownInstanceVersionTitle),
            Strings.Resources_ModUnknownInstanceVersionTitle);
        Assert.NotEqual(
            nameof(Strings.Resources_ModUnknownInstanceVersionMessage),
            Strings.Resources_ModUnknownInstanceVersionMessage);
    }

    [Fact]
    public void LauncherComboBoxStyleAppliesTemplateAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = global::System.Windows.Application.Current;
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MineDock%20Launcher;component/Styles/ControlStyles.xaml",
                        UriKind.Absolute)
                };

                var comboBox = new AnimatedComboBox
                {
                    Style = (Style)dictionary["LauncherComboBoxStyle"],
                    Width = 220,
                    Height = 36,
                    ItemsSource = new[]
                    {
                        new AccountCapeOption { DisplayName = "None", IsNone = true }
                    }
                };

                comboBox.ApplyTemplate();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ListPageFrameHeaderChromeFollowsSearchVisibility()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);
                var frame = new ListPageFrame();

                frame.ApplyTemplate();
                frame.UpdateLayout();
                PumpDispatcher();

                Assert.Equal(132d, frame.HeaderOverlayElement.Height);
                Assert.Equal(140d, VerticalEdgeOpacityMask.GetTopFadeLength(frame.ListLayerElement));
                Assert.Equal(100d, VerticalEdgeOpacityMask.GetTopIntermediateLength(frame.ListLayerElement));

                frame.IsSearchVisible = false;
                frame.UpdateLayout();
                PumpDispatcher();

                Assert.Equal(70d, frame.HeaderOverlayElement.Height);
                Assert.Equal(70d, VerticalEdgeOpacityMask.GetTopFadeLength(frame.ListLayerElement));
                Assert.Equal(50d, VerticalEdgeOpacityMask.GetTopIntermediateLength(frame.ListLayerElement));
                Assert.Equal(0.1d, VerticalEdgeOpacityMask.GetTopIntermediateOpacity(frame.ListLayerElement), 3);

                frame.IsSearchVisible = true;
                frame.UpdateLayout();
                PumpDispatcher();

                Assert.Equal(132d, frame.HeaderOverlayElement.Height);
                Assert.Equal(140d, VerticalEdgeOpacityMask.GetTopFadeLength(frame.ListLayerElement));
                Assert.Equal(100d, VerticalEdgeOpacityMask.GetTopIntermediateLength(frame.ListLayerElement));
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ResourcesModPageKeepsVirtualizedListVisibleWhenEmpty()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);

                var viewModel = new ResourcesPageViewModel();
                var view = new ResourcesModPageView
                {
                    DataContext = viewModel.ModPage,
                    Width = 900,
                    Height = 700
                };
                window = new Window
                {
                    Width = 900,
                    Height = 700,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0
                };

                window.Show();
                view.UpdateLayout();
                PumpDispatcher();

                var listBox = FindVisualDescendant<ListBox>(
                    view,
                    candidate => candidate.Name == "ResourcesModListBox");

                Assert.NotNull(listBox);
                Assert.Empty(viewModel.ModPage.VisibleProjects);
                Assert.Empty(viewModel.ModPage.ProjectListItems);
                Assert.Same(viewModel.ModPage.ProjectListItems, listBox.ItemsSource);
                Assert.Equal(Visibility.Visible, listBox.Visibility);
                Assert.Same(
                    application.TryFindResource("ListPageVirtualizedListBoxStyle"),
                    listBox.Style);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(listBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(listBox));
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ResourcesPageViewStartsModLoadAfterBecomingVisible()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);

                var pendingResult = new TaskCompletionSource<ResourceCatalogSearchResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var service = new PendingResourceCatalogService(pendingResult.Task);
                var viewModel = new ResourcesPageViewModel(service);
                var view = new ResourcesPageView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700,
                    Visibility = Visibility.Collapsed
                };
                window = new Window
                {
                    Width = 900,
                    Height = 700,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0
                };

                window.Show();
                window.UpdateLayout();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.False(view.IsVisible);
                Assert.False(viewModel.ModPage.IsLoadingProjects);
                Assert.Equal(0, service.CallCount);

                view.Visibility = Visibility.Visible;
                window.UpdateLayout();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                Assert.True(viewModel.ModPage.IsLoadingProjects);
                Assert.True(viewModel.ModPage.CanShowLoadingState);
                Assert.Equal(1, service.CallCount);

                var modPageView = FindVisualDescendant<ResourcesModPageView>(view);
                Assert.NotNull(modPageView);
                var listBox = FindVisualDescendant<ListBox>(
                    modPageView,
                    candidate => candidate.Name == "ResourcesModListBox");
                Assert.NotNull(listBox);
                Assert.Equal(Visibility.Visible, listBox.Visibility);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(listBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(listBox));

                pendingResult.SetResult(new ResourceCatalogSearchResult());
                PumpDispatcher(DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void SkinManagerDialogViewInitializesRuntimeContent()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);
                var view = new SkinManagerDialogView();
                view.ApplyTemplate();

                Assert.True(view.MinHeight > 0);
                Assert.NotNull(view.Content);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void ModManagementViewsLoadAtRuntime()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);

                var detailsViewModel = CreateDetailsViewModel();
                detailsViewModel.SetSelectedSection(new GameSettingsDetailSectionItem(
                    "mod_management",
                    Strings.GameSettings_DetailModManagement,
                    "instance_setting_page/mod"));

                var detailsView = new GameSettingsDetailsView
                {
                    DataContext = detailsViewModel,
                    Width = 1200,
                    Height = 800
                };

                detailsView.ApplyTemplate();
                detailsView.Measure(new Size(1200, 800));
                detailsView.Arrange(new Rect(0, 0, 1200, 800));
                detailsView.UpdateLayout();

                var modManagementView = FindVisualDescendant<InstanceModManagementSettingsView>(detailsView);
                Assert.NotNull(modManagementView);

                var pageView = new GameSettingsPageView
                {
                    Width = 1200,
                    Height = 800
                };
                pageView.ApplyTemplate();
                pageView.Measure(new Size(1200, 800));
                pageView.Arrange(new Rect(0, 0, 1200, 800));
                pageView.UpdateLayout();

                Assert.Null(FindVisualDescendantByTag<Border>(pageView, "StickyModListFloatingHost"));

                detailsViewModel.SetSelectedInstance(new GameSettingsInstanceItem(
                    CreateInstance("Fabric Pack", "1.21.4", LoaderKind.Fabric),
                    "release"));
                detailsViewModel.ModManagement.ModSearchQuery = "sodium";
                var standaloneView = new InstanceModManagementSettingsView
                {
                    DataContext = detailsViewModel.ModManagement
                };
                standaloneView.ApplyTemplate();
                standaloneView.Measure(new Size(800, 300));
                standaloneView.Arrange(new Rect(0, 0, 800, 300));
                standaloneView.UpdateLayout();

                Assert.NotNull(standaloneView.Content);
                var searchTextBoxes = FindVisualDescendants<TextBox>(standaloneView).ToArray();
                Assert.Empty(searchTextBoxes);
                var modInfoPanel = FindVisualDescendant<GroupBox>(
                    standaloneView,
                    groupBox => Equals(groupBox.Header, Strings.GameSettings_ModManagementInfoSection));
                Assert.NotNull(modInfoPanel);
                Assert.Equal(Visibility.Visible, modInfoPanel.Visibility);
                Assert.NotNull(FindVisualAncestor<ListBoxItem>(modInfoPanel));

                var modListBox = FindVisualDescendant<ListBox>(standaloneView);
                Assert.NotNull(modListBox);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(modListBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(modListBox));

                detailsViewModel.ModManagement.ModSearchQuery = "lithium";
                standaloneView.UpdateLayout();

                detailsViewModel.SetSelectedInstance(new GameSettingsInstanceItem(
                    CreateInstance("Vanilla World", "1.21.4", LoaderKind.Vanilla),
                    "release"));
                standaloneView.UpdateLayout();
                Assert.Null(FindVisualDescendant<GroupBox>(
                    standaloneView,
                    groupBox => Equals(groupBox.Header, Strings.GameSettings_ModManagementInfoSection)
                        && groupBox.Visibility == Visibility.Visible));

                detailsViewModel.SetSelectedSection(new GameSettingsDetailSectionItem(
                    "saves",
                    Strings.GameSettings_DetailSaves,
                    "instance_setting_page/saves"));
                detailsView.UpdateLayout();

                var saveManagementView = FindVisualDescendant<InstanceSaveManagementSettingsView>(detailsView);
                Assert.NotNull(saveManagementView);

                detailsViewModel.SaveManagement.SaveSearchQuery = "base";
                var standaloneSaveView = new InstanceSaveManagementSettingsView
                {
                    DataContext = detailsViewModel.SaveManagement
                };
                standaloneSaveView.ApplyTemplate();
                standaloneSaveView.Measure(new Size(800, 300));
                standaloneSaveView.Arrange(new Rect(0, 0, 800, 300));
                standaloneSaveView.UpdateLayout();

                Assert.NotNull(standaloneSaveView.Content);
                var saveSearchTextBoxes = FindVisualDescendants<TextBox>(standaloneSaveView).ToArray();
                Assert.Empty(saveSearchTextBoxes);
                var saveInfoPanel = FindVisualDescendant<GroupBox>(
                    standaloneSaveView,
                    groupBox => Equals(groupBox.Header, Strings.GameSettings_SaveManagementInfoSection));
                Assert.NotNull(saveInfoPanel);
                Assert.Equal(Visibility.Visible, saveInfoPanel.Visibility);
                Assert.NotNull(FindVisualAncestor<ListBoxItem>(saveInfoPanel));

                var saveListBox = FindVisualDescendant<ListBox>(standaloneSaveView);
                Assert.NotNull(saveListBox);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(saveListBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(saveListBox));

                detailsViewModel.SetSelectedSection(new GameSettingsDetailSectionItem(
                    "resource_packs",
                    Strings.GameSettings_DetailResourcePacks,
                    "main_menu_library"));
                detailsView.UpdateLayout();

                var resourcePackManagementView = FindVisualDescendant<InstanceResourcePackManagementSettingsView>(detailsView);
                Assert.NotNull(resourcePackManagementView);

                detailsViewModel.ResourcePackManagement.ResourcePackSearchQuery = "fresh";
                var standaloneResourcePackView = new InstanceResourcePackManagementSettingsView
                {
                    DataContext = detailsViewModel.ResourcePackManagement
                };
                standaloneResourcePackView.ApplyTemplate();
                standaloneResourcePackView.Measure(new Size(800, 300));
                standaloneResourcePackView.Arrange(new Rect(0, 0, 800, 300));
                standaloneResourcePackView.UpdateLayout();

                Assert.NotNull(standaloneResourcePackView.Content);
                var resourcePackSearchTextBoxes = FindVisualDescendants<TextBox>(standaloneResourcePackView).ToArray();
                Assert.Empty(resourcePackSearchTextBoxes);
                var resourcePackInfoPanel = FindVisualDescendant<GroupBox>(
                    standaloneResourcePackView,
                    groupBox => Equals(groupBox.Header, Strings.GameSettings_ResourcePackManagementInfoSection));
                Assert.NotNull(resourcePackInfoPanel);
                Assert.Equal(Visibility.Visible, resourcePackInfoPanel.Visibility);
                Assert.NotNull(FindVisualAncestor<ListBoxItem>(resourcePackInfoPanel));

                var resourcePackListBox = FindVisualDescendant<ListBox>(standaloneResourcePackView);
                Assert.NotNull(resourcePackListBox);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(resourcePackListBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(resourcePackListBox));

                detailsViewModel.SetSelectedSection(new GameSettingsDetailSectionItem(
                    "shaders",
                    Strings.GameSettings_DetailShaders,
                    "instance_setting_page/shader"));
                detailsView.UpdateLayout();

                var shaderPackManagementView = FindVisualDescendant<InstanceShaderPackManagementSettingsView>(detailsView);
                Assert.NotNull(shaderPackManagementView);

                detailsViewModel.ShaderPackManagement.ShaderPackSearchQuery = "complementary";
                var standaloneShaderPackView = new InstanceShaderPackManagementSettingsView
                {
                    DataContext = detailsViewModel.ShaderPackManagement
                };
                standaloneShaderPackView.ApplyTemplate();
                standaloneShaderPackView.Measure(new Size(800, 300));
                standaloneShaderPackView.Arrange(new Rect(0, 0, 800, 300));
                standaloneShaderPackView.UpdateLayout();

                Assert.NotNull(standaloneShaderPackView.Content);
                var shaderPackSearchTextBoxes = FindVisualDescendants<TextBox>(standaloneShaderPackView).ToArray();
                Assert.Empty(shaderPackSearchTextBoxes);
                var shaderPackInfoPanel = FindVisualDescendant<GroupBox>(
                    standaloneShaderPackView,
                    groupBox => Equals(groupBox.Header, Strings.GameSettings_ShaderPackManagementInfoSection));
                Assert.NotNull(shaderPackInfoPanel);
                Assert.Equal(Visibility.Visible, shaderPackInfoPanel.Visibility);
                Assert.NotNull(FindVisualAncestor<ListBoxItem>(shaderPackInfoPanel));

                var shaderPackListBox = FindVisualDescendant<ListBox>(standaloneShaderPackView);
                Assert.NotNull(shaderPackListBox);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(shaderPackListBox));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(shaderPackListBox));

                detailsViewModel.SetSelectedSection(new GameSettingsDetailSectionItem(
                    "backup",
                    Strings.GameSettings_DetailBackup,
                    "instance_setting_page/backup"));
                detailsView.UpdateLayout();

                var backupSettingsView = FindVisualDescendant<InstanceBackupSettingsView>(detailsView);
                Assert.NotNull(backupSettingsView);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    [Fact]
    public void GameSettingsTopSearchSwitchesBetweenInstanceListAndResourceManagementSections()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var application = GetOrCreateApplication();
                EnsureApplicationResources(application);

                var instance = CreateInstance("Fabric Pack", "1.21.4", LoaderKind.Fabric);
                var modService = new StubModService();
                modService.ModsByInstanceId[instance.Id] = Enumerable.Range(1, 4)
                    .Select(index => CreateLocalMod($"mod-{index}.jar", instance.InstanceDirectory, $"Mod {index}"))
                    .ToArray();

                var pageViewModel = CreatePageViewModel([instance], modService);
                RunSynchronously(() => pageViewModel.EnsureInstancesLoadedAsync());
                pageViewModel.InstanceSearchQuery = "fabric";

                window = new Window
                {
                    Width = 1280,
                    Height = 900,
                    ShowInTaskbar = false,
                    Content = new GameSettingsPageView
                    {
                        DataContext = pageViewModel,
                        Width = 1280,
                        Height = 900
                    }
                };

                window.Show();
                PumpDispatcher();

                var pageView = Assert.IsType<GameSettingsPageView>(window.Content);
                pageView.UpdateLayout();
                PumpDispatcher();

                var searchBox = FindVisualDescendants<TextBox>(pageView)
                    .Single(textBox => textBox.Tag is Grid);

                Assert.True(searchBox.IsVisible);
                Assert.Equal("fabric", searchBox.Text);

                pageViewModel.SelectInstanceCommand.Execute(pageViewModel.VisibleInstances.Single());
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.False(searchBox.IsVisible);

                pageViewModel.SelectDetailsSectionCommand.Execute(
                    pageViewModel.DetailSections.Single(section => section.Id == "mod_management"));
                pageViewModel.Details.ModManagement.ModSearchQuery = "sodium";
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.True(searchBox.IsVisible);
                Assert.Equal("sodium", searchBox.Text);
                Assert.Contains(
                    FindVisualDescendants<Button>(pageView),
                    button => button.IsVisible
                        && ReferenceEquals(
                            button.Command,
                            pageViewModel.Details.ModManagement.ToggleMultiSelectModeCommand));

                searchBox.Text = "lithium";
                PumpDispatcher();

                Assert.Equal("lithium", pageViewModel.Details.ModManagement.ModSearchQuery);

                pageViewModel.SelectDetailsSectionCommand.Execute(
                    pageViewModel.DetailSections.Single(section => section.Id == "saves"));
                pageViewModel.Details.SaveManagement.SaveSearchQuery = "base";
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.True(searchBox.IsVisible);
                Assert.Equal("base", searchBox.Text);
                Assert.Contains(
                    FindVisualDescendants<Button>(pageView),
                    button => button.IsVisible
                        && ReferenceEquals(
                            button.Command,
                            pageViewModel.Details.SaveManagement.ToggleMultiSelectModeCommand));

                searchBox.Text = "world";
                PumpDispatcher();

                Assert.Equal("world", pageViewModel.Details.SaveManagement.SaveSearchQuery);

                pageViewModel.SelectDetailsSectionCommand.Execute(
                    pageViewModel.DetailSections.Single(section => section.Id == "resource_packs"));
                pageViewModel.Details.ResourcePackManagement.ResourcePackSearchQuery = "fresh";
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.True(searchBox.IsVisible);
                Assert.Equal("fresh", searchBox.Text);
                Assert.Contains(
                    FindVisualDescendants<Button>(pageView),
                    button => button.IsVisible
                        && ReferenceEquals(
                            button.Command,
                            pageViewModel.Details.ResourcePackManagement.ToggleMultiSelectModeCommand));

                searchBox.Text = "bare";
                PumpDispatcher();

                Assert.Equal("bare", pageViewModel.Details.ResourcePackManagement.ResourcePackSearchQuery);

                pageViewModel.SelectDetailsSectionCommand.Execute(
                    pageViewModel.DetailSections.Single(section => section.Id == "shaders"));
                pageViewModel.Details.ShaderPackManagement.ShaderPackSearchQuery = "complementary";
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.True(searchBox.IsVisible);
                Assert.Equal("complementary", searchBox.Text);
                Assert.Contains(
                    FindVisualDescendants<Button>(pageView),
                    button => button.IsVisible
                        && ReferenceEquals(
                            button.Command,
                            pageViewModel.Details.ShaderPackManagement.ToggleMultiSelectModeCommand));

                searchBox.Text = "bsl";
                PumpDispatcher();

                Assert.Equal("bsl", pageViewModel.Details.ShaderPackManagement.ShaderPackSearchQuery);

                pageViewModel.BackToInstanceListCommand.Execute(null);
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.True(searchBox.IsVisible);
                Assert.Equal("fabric", searchBox.Text);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                window?.Close();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

#if false
    [Fact]
    public void ModManagementStringsResolveAtRuntime()
    {
        Assert.Equal("Mod信息", Strings.GameSettings_ModManagementInfoSection);
        Assert.Equal("当前游戏已安�?0 �?mods，已启用 0 �?, string.Format(Strings.GameSettings_ModManagementInstalledSummaryFormat, 0, 0));
        Assert.Equal("打开 mod 文件�?, Strings.GameSettings_ModManagementOpenFolderButton);
    }

#endif

    [Fact]
    public void ModManagementStringsResolveAtRuntime()
    {
        Assert.Equal("Mod\u4fe1\u606f", Strings.GameSettings_ModManagementInfoSection);
        Assert.Equal(
            "\u5f53\u524d\u6e38\u620f\u5df2\u5b89\u88c5 0 \u4e2a mods\uff0c\u5df2\u542f\u7528 0 \u4e2a",
            string.Format(Strings.GameSettings_ModManagementInstalledSummaryFormat, 0, 0));
        Assert.Equal(
            "\u6253\u5f00 mod \u6587\u4ef6\u5939",
            Strings.GameSettings_ModManagementOpenFolderButton);
        Assert.Equal("\u7b5b\u9009", Strings.GameSettings_ModManagementFilterLabel);
        Assert.Equal("\u5168\u90e8", Strings.GameSettings_ModManagementFilterAllButton);
        Assert.Equal("\u5df2\u542f\u7528", Strings.GameSettings_ModManagementFilterEnabledButton);
        Assert.Equal("\u5df2\u7981\u7528", Strings.GameSettings_ModManagementFilterDisabledButton);
    }

    private static void EnsureApplicationResources(global::System.Windows.Application application)
    {
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MineDock%20Launcher;component/Styles/ControlStyles.xaml",
                UriKind.Absolute)
        });
        application.Resources["BooleanToMenuTextVisibilityConverter"] = new BooleanToMenuTextVisibilityConverter();
        application.Resources["SkinActiveStateVisibilityConverter"] = new SkinActiveStateVisibilityConverter();
    }

    [Fact]
    public void AccentResourceDictionaryOverridesAccentWithoutChangingDangerResources()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = GetOrCreateApplication();
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
                application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
                application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Accents/Orange.xaml"));

                Assert.Equal("#FFF97316", ((SolidColorBrush)application.TryFindResource("LauncherAccentBrush")!).Color.ToString());
                Assert.Equal("#FFD94343", ((SolidColorBrush)application.TryFindResource("Brush.Danger.Primary")!).Color.ToString());
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static GameSettingsDetailsViewModel CreateDetailsViewModel()
    {
        var statusService = new StubStatusService();
        var instanceService = new StubGameInstanceService();
        var editDialog = new GameSettingsEditDialogViewModel(instanceService, statusService);

        return new GameSettingsDetailsViewModel(
            editDialog,
            instanceService,
            statusService,
            new StubInstanceFolderService(),
            new StubSystemMemoryService(),
            new StubModService(),
            new StubInstanceBackupService(),
            new LocalModsViewModel(new StubModService(), statusService),
            new LocalSavesViewModel(new StubSaveService(), statusService),
            new LocalResourcePacksViewModel(new StubResourcePackService(), statusService),
            new LocalShaderPacksViewModel(new StubShaderPackService(), statusService),
            new StubJavaRuntimeDiscoveryService(),
            new StubFilePickerService(),
            new StubFloatingMessageService());
    }

    private static GameSettingsPageViewModel CreatePageViewModel(
        IReadOnlyList<GameInstance> instances,
        StubModService modService)
    {
        var statusService = new StubStatusService();
        var instanceService = new StubGameInstanceService();
        foreach (var instance in instances)
            instanceService.Instances.Add(instance);

        return new GameSettingsPageViewModel(
            instanceService,
            new StubGameVersionService(),
            statusService,
            new StubInstanceFolderService(),
            new StubSystemMemoryService(),
            modService,
            new StubInstanceBackupService(),
            new LocalModsViewModel(modService, statusService),
            new LocalSavesViewModel(new StubSaveService(), statusService),
            new LocalResourcePacksViewModel(new StubResourcePackService(), statusService),
            new LocalShaderPacksViewModel(new StubShaderPackService(), statusService),
            new StubJavaRuntimeDiscoveryService(),
            new StubFilePickerService(),
            new StubFloatingMessageService());
    }

    private static GameInstance CreateInstance(string name, string minecraftVersion, LoaderKind loader)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            VersionName = name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader is LoaderKind.Vanilla ? null : "0.16.10",
            Description = string.Empty,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private static LocalMod CreateLocalMod(string fileName, string instanceDirectory, string displayName)
    {
        return new LocalMod
        {
            Name = displayName,
            Loader = "fabric",
            ModId = Path.GetFileNameWithoutExtension(fileName),
            Version = "1.0.0",
            FileName = fileName,
            FullPath = Path.Combine(instanceDirectory, "mods", fileName),
            IsEnabled = true,
            SizeBytes = 1024,
            Source = "Local"
        };
    }

    private static void PumpDispatcher()
    {
        PumpDispatcher(DispatcherPriority.Background);
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static void RunSynchronously(Func<Task> action)
    {
        action().GetAwaiter().GetResult();
    }
    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
            return typedRoot;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var result = FindVisualDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (result is not null)
                return result;
        }

        return null;
    }

    private static T? FindVisualDescendantByTag<T>(DependencyObject root, object tag)
        where T : FrameworkElement
    {
        return FindVisualDescendant<T>(root, element => Equals(element.Tag, tag));
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T typedRoot && predicate(typedRoot))
            return typedRoot;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var result = FindVisualDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T typedCurrent)
                return typedCurrent;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
            yield return typedRoot;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            foreach (var descendant in FindVisualDescendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return descendant;
        }
    }

    private static ResourceDictionary LoadDictionary(string relativePath)
    {
        return new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/MineDock%20Launcher;component/{relativePath}",
                UriKind.Absolute)
        };
    }

    private static global::System.Windows.Application GetOrCreateApplication()
    {
        return WpfApplicationTestHelper.GetOrCreateApplication();
    }

    private sealed class PendingResourceCatalogService(Task<ResourceCatalogSearchResult> resultTask) : IResourceCatalogService
    {
        public int CallCount { get; private set; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return resultTask;
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResourceProjectVersionsResult());
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("installed.jar");
        }

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("downloaded.jar");
        }

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class StubStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            MessageReported?.Invoke(message);
        }
    }

    private sealed class StubInstanceFolderService : IInstanceFolderService
    {
        public bool DirectoryExists(string folderPath) => true;

        public string EnsureDirectoryExists(string folderPath) => folderPath;

        public bool TryOpen(string folderPath) => true;

        public bool TryRevealFile(string filePath) => true;
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public string? PickMinecraftSkin() => null;

        public string? PickJavaExecutable() => null;

        public string? PickModFile() => null;

        public string? PickSaveArchive() => null;

        public string? PickResourcePackArchive() => null;

        public string? PickShaderPackArchive() => null;

        public string? PickLocalImportFile() => null;

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    private sealed class StubFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public void Show(string message)
        {
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class StubSystemMemoryService : ISystemMemoryService
    {
        public SystemMemorySnapshot GetSnapshot()
        {
            return new SystemMemorySnapshot(
                16L * 1024L * 1024L * 1024L,
                8L * 1024L * 1024L * 1024L);
        }
    }

    private sealed class StubGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult<IReadOnlyList<MinecraftVersionInfo>>([]);
        }
    }

    private sealed class StubJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<JavaRuntimeInfo>>([]);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new JavaRuntimeInfo(
                "Java",
                null,
                null,
                "unknown",
                executablePath,
                string.Empty,
                "Test"));
        }
    }

    private sealed class StubModService : IModService
    {
        public Dictionary<string, IReadOnlyList<LocalMod>> ModsByInstanceId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ModsByInstanceId.TryGetValue(instance.Id, out var mods)
                    ? mods
                    : (IReadOnlyList<LocalMod>)[]);
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubSaveService : ILocalSaveService
    {
        public Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalSave>>([]);
        }

        public Task<LocalSaveImportResult> ImportFromArchiveAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubInstanceBackupService : IInstanceBackupService
    {
        public Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(backupDirectory);
        }

        public Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
            string backupDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<InstanceBackupRecord>>([]);
        }

        public Task<InstanceBackupRecord> CreateBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceBackupRecord
            {
                Name = backupName,
                FileName = $"{backupName}.zip",
                FullPath = Path.Combine(backupDirectory, $"{backupName}.zip"),
                SizeBytes = 1024 * 1024,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task DeleteBackupAsync(
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RestoreBackupAsync(
            GameInstance instance,
            string backupDirectory,
            string backupFullPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubResourcePackService : ILocalResourcePackService
    {
        public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalResourcePack>>([]);
        }

        public Task<LocalResourcePackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubShaderPackService : ILocalShaderPackService
    {
        public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalShaderPack>>([]);
        }

        public Task<LocalShaderPackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnsupportedArchive));
        }

        public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubGameInstanceService : IGameInstanceService
    {
        public List<GameInstance> Instances { get; } = [];

        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GameInstance>>(Instances);
        }

        public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GameInstance?>(null);
        }

        public Task<GameInstance> CreateInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string? name,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0,
            bool installFabricApi = true,
            string? fabricApiVersionId = null,
            string? quiltStandardLibraryVersionId = null)
        {
            throw new NotSupportedException();
        }

        public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GameInstance> RenameInstanceAsync(string instanceId, string? newName, string? newIconSource, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

