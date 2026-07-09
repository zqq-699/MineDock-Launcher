using Launcher.App.Resources;

namespace Launcher.App.ViewModels.GameSettings;

internal static class GameSettingsDetailSectionFactory
{
    public static IReadOnlyList<GameSettingsDetailSectionItem> Create()
    {
        return
        [
            new GameSettingsDetailSectionItem("general", Strings.GameSettings_DetailGeneral, "instance_setting_page/general_setting"),
            new GameSettingsDetailSectionItem("launch", Strings.GameSettings_DetailLaunch, "instance_setting_page/launch"),
            new GameSettingsDetailSectionItem("java", Strings.GameSettings_DetailJava, "instance_setting_page/java"),
            new GameSettingsDetailSectionItem("mod_management", Strings.GameSettings_DetailModManagement, "instance_setting_page/mod"),
            new GameSettingsDetailSectionItem("saves", Strings.GameSettings_DetailSaves, "instance_setting_page/saves"),
            new GameSettingsDetailSectionItem("resource_packs", Strings.GameSettings_DetailResourcePacks, "main_menu_library"),
            new GameSettingsDetailSectionItem("shaders", Strings.GameSettings_DetailShaders, "instance_setting_page/shader"),
            new GameSettingsDetailSectionItem("backup", Strings.GameSettings_DetailBackup, "instance_setting_page/backup"),
            new GameSettingsDetailSectionItem("export", Strings.GameSettings_DetailExport, "instance_setting_page/export")
        ];
    }
}
