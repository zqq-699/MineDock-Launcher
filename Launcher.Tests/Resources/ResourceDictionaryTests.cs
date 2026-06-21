using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.App.Views.GameSettings;
using Launcher.App.Views.Account.Dialogs;
using Launcher.Application.Services;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Resources;

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
                        "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
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
                _ = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
                var shared = LoadDictionary("Resources/Themes/Shared.xaml");
                var dark = LoadDictionary("Resources/Themes/Dark.xaml");
                var light = LoadDictionary("Resources/Themes/Light.xaml");
                var purpleAccent = LoadDictionary("Resources/Themes/Accents/Purple.xaml");

                Assert.NotNull(shared["LauncherFontFamily"]);
                Assert.True(Assert.IsType<bool>(shared["Is.BackdropBlur.Enabled"]));
                Assert.NotNull(dark["Brush.Text.Primary"]);
                Assert.NotNull(light["Brush.Text.Primary"]);
                Assert.NotNull(dark["Brush.Icon.Primary"]);
                Assert.NotNull(light["Brush.Icon.Primary"]);
                Assert.Equal("#5A4A4A4A", ((SolidColorBrush)dark["Brush.SecondaryMenu.Panel"]).Color.ToString());
                Assert.Equal("#CC252525", ((SolidColorBrush)dark["Brush.Surface.Popup"]).Color.ToString());
                Assert.Equal("#66252525", ((SolidColorBrush)dark["Brush.Surface.PopupTint"]).Color.ToString());
                Assert.Equal("#33252525", ((SolidColorBrush)dark["Brush.Surface.PopupBlurTint"]).Color.ToString());
                Assert.Equal("#FF181818", ((SolidColorBrush)dark["Brush.Page.Background"]).Color.ToString());
                Assert.Equal(0.85d, Assert.IsType<double>(dark["Opacity.Page.Background"]), 3);
                Assert.Equal("#80FFFFFF", ((SolidColorBrush)light["Brush.Surface.Popup"]).Color.ToString());
                Assert.Equal("#40FFFFFF", ((SolidColorBrush)light["Brush.Surface.PopupTint"]).Color.ToString());
                Assert.Equal("#33FFFFFF", ((SolidColorBrush)light["Brush.Surface.PopupBlurTint"]).Color.ToString());
                Assert.Equal("#5AFFFFFF", ((SolidColorBrush)light["Brush.SecondaryMenu.Panel"]).Color.ToString());
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
                Assert.Equal("#80E8EDF4", ((SolidColorBrush)light["Brush.List.Item.Selected"]).Color.ToString());
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
                        "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
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
    public void SkinManagerDialogViewInitializesRuntimeContent()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
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
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
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

                Assert.NotNull(FindVisualDescendantByTag<Border>(pageView, "StickyModListFloatingHost"));

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
                Assert.Null(standaloneView.FindName("StickyModListOverlay"));

                var searchTextBoxes = FindVisualDescendants<TextBox>(standaloneView).ToArray();
                Assert.Single(searchTextBoxes);
                Assert.Equal("sodium", searchTextBoxes[0].Text);

                detailsViewModel.ModManagement.ModSearchQuery = "lithium";
                standaloneView.UpdateLayout();

                Assert.Equal("lithium", searchTextBoxes[0].Text);

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
                Assert.Single(saveSearchTextBoxes);
                Assert.Equal("base", saveSearchTextBoxes[0].Text);
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
    public void ModManagementStickyHeaderActivatesEvenWhenFloatingLayerStartsHidden()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
                EnsureApplicationResources(application);

                var instance = CreateInstance("Fabric Pack", "1.21.4", LoaderKind.Fabric);
                var modService = new StubModService();
                modService.ModsByInstanceId[instance.Id] = Enumerable.Range(1, 24)
                    .Select(index => CreateLocalMod($"mod-{index}.jar", instance.InstanceDirectory, $"Mod {index}"))
                    .ToArray();

                var pageViewModel = CreatePageViewModel([instance], modService);
                RunSynchronously(() => pageViewModel.EnsureInstancesLoadedAsync());
                pageViewModel.SelectInstanceCommand.Execute(pageViewModel.VisibleInstances.Single());
                pageViewModel.SelectDetailsSectionCommand.Execute(
                    pageViewModel.DetailSections.Single(section => section.Id == "mod_management"));

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

                var floatingLayer = FindVisualDescendantByTag<Grid>(pageView, "StickyModListFloatingLayer");
                var detailsView = FindVisualDescendant<GameSettingsDetailsView>(pageView);
                var modManagementView = FindVisualDescendant<InstanceModManagementSettingsView>(pageView);

                Assert.NotNull(floatingLayer);
                Assert.NotNull(detailsView);
                Assert.NotNull(modManagementView);
                Assert.Equal(Visibility.Hidden, floatingLayer.Visibility);
                Assert.Equal(1d, modManagementView.OriginalModListHeaderElement.Opacity);

                var detailsScrollViewer = detailsView.ScrollViewerControl;
                for (var offset = 0d; offset <= 1200d && floatingLayer.Visibility != Visibility.Visible; offset += 40d)
                {
                    detailsScrollViewer.ScrollToVerticalOffset(offset);
                    detailsScrollViewer.UpdateLayout();
                    pageView.UpdateLayout();
                    PumpDispatcher();
                }

                Assert.Equal(Visibility.Visible, floatingLayer.Visibility);
                Assert.True(floatingLayer.IsHitTestVisible);
                Assert.Equal(0d, modManagementView.OriginalModListHeaderElement.Opacity);

                pageViewModel.BackToInstanceListCommand.Execute(null);
                pageView.UpdateLayout();
                PumpDispatcher();

                Assert.Equal(Visibility.Hidden, floatingLayer.Visibility);
                Assert.False(floatingLayer.IsHitTestVisible);
                Assert.Equal(1d, modManagementView.OriginalModListHeaderElement.Opacity);
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
        Assert.Equal("当前游戏已安装 0 个 mods，已启用 0 个", string.Format(Strings.GameSettings_ModManagementInstalledSummaryFormat, 0, 0));
        Assert.Equal("打开 mod 文件夹", Strings.GameSettings_ModManagementOpenFolderButton);
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
    }

    private static void EnsureApplicationResources(global::System.Windows.Application application)
    {
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Launcher.App;component/Styles/ControlStyles.xaml",
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
                var application = global::System.Windows.Application.Current ?? new global::System.Windows.Application();
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
            new LocalModsViewModel(new StubModService(), statusService),
            new LocalSavesViewModel(new StubSaveService(), statusService),
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
            new LocalModsViewModel(modService, statusService),
            new LocalSavesViewModel(new StubSaveService(), statusService),
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
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
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
                $"pack://application:,,,/Launcher.App;component/{relativePath}",
                UriKind.Absolute)
        };
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

        public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
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
            int downloadSpeedLimitMbPerSecond = 0)
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
