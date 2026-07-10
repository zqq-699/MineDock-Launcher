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

using System.Windows.Media.Effects;

namespace Launcher.App.Effects;

internal static class ProgressiveGaussianBlurShader
{
    internal const string PackUri =
        "pack://application:,,,/BlockHelm_Launcher_x64;component/Effects/Shaders/ProgressiveGaussianBlur.ps";

    private static readonly object SyncRoot = new();
    private static PixelShader? pixelShader;
    private static Exception? initializationException;
    private static bool initializationAttempted;

    internal static bool TryGet(out PixelShader? shader, out Exception? exception)
    {
        lock (SyncRoot)
        {
            if (!initializationAttempted)
                Initialize();

            shader = pixelShader;
            exception = initializationException;
            return shader is not null;
        }
    }

    private static void Initialize()
    {
        initializationAttempted = true;
        try
        {
            var loadedShader = new PixelShader
            {
                ShaderRenderMode = ShaderRenderMode.HardwareOnly,
                UriSource = new Uri(PackUri, UriKind.Absolute)
            };
            loadedShader.Freeze();
            pixelShader = loadedShader;
        }
        catch (Exception exception)
        {
            initializationException = exception;
        }
    }
}
