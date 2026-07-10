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
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftSkinFileValidator : IMinecraftSkinFileValidator
{
    public Task<MinecraftSkinFileValidationResult> ValidateAsync(
        string skinFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(skinFilePath))
            return Task.FromResult(new MinecraftSkinFileValidationResult(false, 0, 0));

        try
        {
            using var stream = File.OpenRead(skinFilePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            var width = frame?.PixelWidth ?? 0;
            var height = frame?.PixelHeight ?? 0;
            var isValid = width == 64 && (height == 64 || height == 32);
            return Task.FromResult(new MinecraftSkinFileValidationResult(isValid, width, height));
        }
        catch
        {
            return Task.FromResult(new MinecraftSkinFileValidationResult(false, 0, 0));
        }
    }
}
