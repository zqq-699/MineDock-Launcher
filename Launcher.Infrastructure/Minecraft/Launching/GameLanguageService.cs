/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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
