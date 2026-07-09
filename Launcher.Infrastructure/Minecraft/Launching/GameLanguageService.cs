using System.IO;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class GameLanguageService : IGameLanguageService
{
    public async Task ApplyLauncherLanguageAsync(
        GameInstance instance,
        string launcherLanguage,
        CancellationToken cancellationToken = default)
    {
        var instanceDirectory = Path.GetFullPath(instance.InstanceDirectory);
        Directory.CreateDirectory(instanceDirectory);

        var optionsPath = Path.Combine(instanceDirectory, "options.txt");
        var minecraftLanguage = ResolveMinecraftLanguage(launcherLanguage);

        if (!File.Exists(optionsPath))
        {
            await File.WriteAllTextAsync(optionsPath, $"lang:{minecraftLanguage}{Environment.NewLine}", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var lines = (await File.ReadAllLinesAsync(optionsPath, cancellationToken).ConfigureAwait(false)).ToList();
        var languageLineIndex = lines.FindIndex(line => line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
        if (languageLineIndex >= 0)
            lines[languageLineIndex] = $"lang:{minecraftLanguage}";
        else
            lines.Add($"lang:{minecraftLanguage}");

        await File.WriteAllLinesAsync(optionsPath, lines, cancellationToken).ConfigureAwait(false);
    }

    internal static string ResolveMinecraftLanguage(string? launcherLanguage)
    {
        return LauncherLanguages.Normalize(launcherLanguage) switch
        {
            LauncherLanguages.English => "en_us",
            LauncherLanguages.TraditionalChinese => "zh_tw",
            LauncherLanguages.Japanese => "ja_jp",
            _ => "zh_cn"
        };
    }
}
