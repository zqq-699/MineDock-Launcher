using System.Windows;
using System.Windows.Media;
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

                var standaloneView = new InstanceModManagementSettingsView
                {
                    DataContext = detailsViewModel.ModManagement
                };
                standaloneView.ApplyTemplate();
                standaloneView.Measure(new Size(800, 300));
                standaloneView.Arrange(new Rect(0, 0, 800, 300));
                standaloneView.UpdateLayout();

                Assert.NotNull(standaloneView.Content);
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
            new StubJavaRuntimeDiscoveryService(),
            new StubFilePickerService(),
            new StubFloatingMessageService());
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
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public string? PickMinecraftSkin() => null;

        public string? PickJavaExecutable() => null;

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
        public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalMod>>([]);
        }

        public Task<LocalMod> ImportAsync(GameInstance instance, string sourceJarPath, CancellationToken cancellationToken = default)
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

    private sealed class StubGameInstanceService : IGameInstanceService
    {
        public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GameInstance>>([]);
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
