using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Settings;

public sealed class ListPreviewSettingsViewModel : SettingsSectionViewModelBase
{
    private static readonly string[] PreviewIconKeys =
    [
        "general/general_all_application",
        "instance_setting_page/mod",
        "instance_setting_page/loader",
        "instance_setting_page/shader",
        "instance_setting_page/saves",
        "instance_setting_page/list_view"
    ];

    public ListPreviewSettingsViewModel(SettingsPageViewModel parent)
        : base(parent)
    {
        Items = Enumerable.Range(1, 48)
            .Select(index => new SettingsListPreviewItem(
                string.Format(Strings.Settings_ListPreviewItemTitleFormat, index),
                Strings.Settings_ListPreviewItemSubtitle,
                string.Format(Strings.Settings_ListPreviewItemTrailingFormat, index),
                PreviewIconKeys[(index - 1) % PreviewIconKeys.Length]))
            .ToArray();
    }

    public IReadOnlyList<SettingsListPreviewItem> Items { get; }
}
