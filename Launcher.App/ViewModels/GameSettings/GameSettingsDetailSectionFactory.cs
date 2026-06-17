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
            new GameSettingsDetailSectionItem("java_memory", Strings.GameSettings_DetailJavaMemory, "instance_setting_page/java"),
            new GameSettingsDetailSectionItem("mod_management", Strings.GameSettings_DetailModManagement, "instance_setting_page/mod"),
            new GameSettingsDetailSectionItem("saves", Strings.GameSettings_DetailSaves, "instance_setting_page/saves"),
            new GameSettingsDetailSectionItem("shaders", Strings.GameSettings_DetailShaders, "instance_setting_page/shader"),
            new GameSettingsDetailSectionItem("loader", Strings.GameSettings_DetailLoader, "instance_setting_page/loader"),
            new GameSettingsDetailSectionItem("advanced", Strings.GameSettings_DetailAdvanced, "instance_setting_page/advanced_setting"),
            new GameSettingsDetailSectionItem("backup", Strings.GameSettings_DetailBackup, "instance_setting_page/backup")
        ];
    }
}
