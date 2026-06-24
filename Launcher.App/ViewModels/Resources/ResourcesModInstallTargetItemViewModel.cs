using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Shared;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModInstallTargetItemViewModel
{
    private ResourcesModInstallTargetItemViewModel(
        GameInstance? instance,
        string title,
        string subtitle,
        string? iconSource,
        string? iconKey,
        bool isLocalDownload)
    {
        Instance = instance;
        Title = title;
        Subtitle = subtitle;
        IconSource = iconSource;
        IconKey = iconKey;
        IsLocalDownload = isLocalDownload;
    }

    public GameInstance? Instance { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string? IconSource { get; }

    public string? IconKey { get; }

    public bool IsLocalDownload { get; }

    public bool IsFirstVisible { get; private set; }

    public bool IsLastVisible { get; private set; }

    public bool IsPreviousItemHighlighted => false;

    public void SetVisiblePosition(bool isFirstVisible, bool isLastVisible)
    {
        IsFirstVisible = isFirstVisible;
        IsLastVisible = isLastVisible;
    }

    public static ResourcesModInstallTargetItemViewModel FromInstance(GameInstance instance)
    {
        return new ResourcesModInstallTargetItemViewModel(
            instance,
            GameInstanceDisplayFormatter.GetName(instance),
            GameInstanceDisplayFormatter.GetSubtitle(instance),
            MinecraftVersionIconResolver.Resolve(instance, instance.VersionType, instance.MinecraftVersion),
            iconKey: null,
            isLocalDownload: false);
    }

    public static ResourcesModInstallTargetItemViewModel CreateLocalDownload()
    {
        return new ResourcesModInstallTargetItemViewModel(
            instance: null,
            Strings.Resources_ModInstallTargetLocal,
            subtitle: string.Empty,
            iconSource: null,
            iconKey: "main_menu_instance_download",
            isLocalDownload: true);
    }
}
