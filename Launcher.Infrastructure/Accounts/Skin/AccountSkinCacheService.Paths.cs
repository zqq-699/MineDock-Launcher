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
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal sealed partial class AccountSkinCacheService
{
private string CreateSkinPath(string uuid, string contentHash)
    {
        var accountSkinDirectory = GetAccountSkinDirectory(uuid);
        Directory.CreateDirectory(accountSkinDirectory);
        var safeHash = contentHash.Length > 24 ? contentHash[..24] : contentHash;
        return Path.Combine(accountSkinDirectory, $"{SkinCacheVersion}-{safeHash}.png");
    }

    private string? GetLatestCachedSkinPath(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var accountSkinDirectory = GetAccountSkinDirectory(uuid);
        if (Directory.Exists(accountSkinDirectory))
        {
            var accountSkinPath = Directory.EnumerateFiles(accountSkinDirectory, "*.png")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
            if (accountSkinPath is not null)
                return accountSkinPath;
        }

        if (!Directory.Exists(skinDirectory))
            return null;

        return Directory.EnumerateFiles(skinDirectory, $"{uuid}-{SkinCacheVersion}-*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private string GetAccountSkinDirectory(string uuid)
    {
        return Path.Combine(skinDirectory, SanitizePathSegment(uuid));
    }

    private static LauncherSkinRecord? FindExisting(
        IReadOnlyList<LauncherSkinRecord> skins,
        string contentHash,
        MinecraftSkinModel skinModel)
    {
        return skins.FirstOrDefault(skin =>
            skin.SkinModel == skinModel
            && (string.Equals(skin.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                || SourceContentMatchesHash(skin.Source, contentHash)));
    }

    private static LauncherSkinRecord? FindExistingBySource(
        IReadOnlyList<LauncherSkinRecord> skins,
        string skinPath)
    {
        return skins.FirstOrDefault(skin =>
            ResolveSkinSourcePath(skin.Source) is { } sourcePath
            && string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(skinPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private static MinecraftSkinModel? FindModelForFile(
        IReadOnlyList<LauncherSkinRecord> skins,
        string skinPath,
        string contentHash)
    {
        var sourceMatch = FindExistingBySource(skins, skinPath);
        if (sourceMatch is not null)
            return sourceMatch.SkinModel;

        return skins.FirstOrDefault(skin =>
                string.Equals(skin.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            ?.SkinModel;
    }
}
