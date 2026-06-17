using Launcher.App.Resources;
using Launcher.App.ViewModels.Shared;

namespace Launcher.App.ViewModels.GameSettings;

internal static class GameSettingsIconOptionFactory
{
    public static IReadOnlyList<GameSettingsIconOption> Create()
    {
        return
        [
            new GameSettingsIconOption(Strings.GameSettings_IconGrassBlock, MinecraftVersionIconResolver.DefaultReleaseIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconDirtBlock, MinecraftVersionIconResolver.DefaultSnapshotIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconCraftingTable, MinecraftVersionIconResolver.DefaultBetaIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconStoneBlock, MinecraftVersionIconResolver.DefaultAlphaIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconFabric, MinecraftVersionIconResolver.DefaultFabricIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconAnvil, MinecraftVersionIconResolver.DefaultForgeIconSource),
            new GameSettingsIconOption(Strings.GameSettings_IconDiamondBlock, "/Assets/Icons/block/diamond_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconBeacon, "/Assets/Icons/block/Beacon_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconFurnace, "/Assets/Icons/block/Furnace_block.png"),
            new GameSettingsIconOption(Strings.GameSettings_IconTnt, "/Assets/Icons/block/TNT_block.png")
        ];
    }
}
