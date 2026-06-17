using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class LauncherVersionMetadata
{
    private const string LauncherPropertyName = "launcher";
    private const string MinecraftVersionPropertyName = "minecraftVersion";

    public static void Apply(JsonObject versionJson, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return;

        versionJson[LauncherPropertyName] = new JsonObject
        {
            [MinecraftVersionPropertyName] = minecraftVersion
        };
    }

    public static string ReadMinecraftVersion(JsonElement root)
    {
        if (!root.TryGetProperty(LauncherPropertyName, out var launcher)
            || launcher.ValueKind is not JsonValueKind.Object)
        {
            return string.Empty;
        }

        return TryGetStringProperty(launcher, MinecraftVersionPropertyName);
    }

    private static string TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
