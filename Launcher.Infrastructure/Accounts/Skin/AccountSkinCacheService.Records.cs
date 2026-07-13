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
private static LauncherSkinRecord CreateRecord(
        string contentHash,
        MinecraftSkinModel skinModel,
        string source)
    {
        return new LauncherSkinRecord
        {
            Id = $"skin-{contentHash[..Math.Min(16, contentHash.Length)]}-{skinModel.ToString().ToLowerInvariant()}",
            Source = source,
            SkinModel = skinModel,
            ContentHash = contentHash,
            AddedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static LauncherSkinRecord CopyRecordWithSource(
        LauncherSkinRecord skin,
        string source,
        string contentHash)
    {
        return new LauncherSkinRecord
        {
            Id = skin.Id,
            Source = source,
            SkinModel = skin.SkinModel,
            ContentHash = string.IsNullOrWhiteSpace(skin.ContentHash) ? contentHash : skin.ContentHash,
            AddedAtUtc = skin.AddedAtUtc
        };
    }

    private static string? ResolveSkinSourcePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : null;

        return source;
    }

    private static bool SourceContentMatchesHash(string source, string contentHash)
    {
        // 哈希匹配需要读取实际文件，路径或 URL 字符串本身不能代表皮肤内容身份。
        var sourcePath = ResolveSkinSourcePath(source);
        if (sourcePath is null || !File.Exists(sourcePath))
            return false;

        try
        {
            return string.Equals(
                ComputeSkinContentHash(File.ReadAllBytes(sourcePath)),
                contentHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static MinecraftSkinModel? TryParseSkinModel(string skinPath)
    {
        // 文件名模型后缀只作为历史兼容提示，无法识别时交由调用方使用默认模型。
        var name = Path.GetFileNameWithoutExtension(skinPath);
        if (name.EndsWith("-slim", StringComparison.OrdinalIgnoreCase))
            return MinecraftSkinModel.Slim;

        if (name.EndsWith("-classic", StringComparison.OrdinalIgnoreCase))
            return MinecraftSkinModel.Classic;

        return null;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }

    private static bool IsPathInDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string ComputeSkinContentHash(byte[] bytes)
    {
        try
        {
            var bitmap = DecodeSkinBitmap(bytes);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            var hashInput = new byte[8 + pixels.Length];
            BitConverter.GetBytes(bitmap.PixelWidth).CopyTo(hashInput, 0);
            BitConverter.GetBytes(bitmap.PixelHeight).CopyTo(hashInput, 4);
            pixels.CopyTo(hashInput, 8);
            return ComputeHash(hashInput);
        }
        catch
        {
            return ComputeHash(bytes);
        }
    }
}
