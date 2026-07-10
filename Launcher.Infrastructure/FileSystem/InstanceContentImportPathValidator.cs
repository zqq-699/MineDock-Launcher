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

namespace Launcher.Infrastructure.FileSystem;

public sealed class InstanceContentImportPathValidator : IInstanceContentImportPathValidator
{
    private static readonly string[] SaveArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".tar.gz",
        ".tar.bz2",
        ".tbz2"
    ];

    public InstanceContentImportPathValidation Validate(
        IReadOnlyList<string> paths,
        InstanceContentImportKind contentKind)
    {
        if (paths.Count == 0)
            return new InstanceContentImportPathValidation(InstanceContentImportPathFailure.Empty);

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                return new InstanceContentImportPathValidation(
                    InstanceContentImportPathFailure.DirectoryNotSupported);
            }

            if (!File.Exists(path) || !HasSupportedExtension(path, contentKind))
            {
                return new InstanceContentImportPathValidation(
                    InstanceContentImportPathFailure.MissingOrInvalidFile);
            }
        }

        return new InstanceContentImportPathValidation(InstanceContentImportPathFailure.None);
    }

    private static bool HasSupportedExtension(string path, InstanceContentImportKind contentKind)
    {
        return contentKind switch
        {
            InstanceContentImportKind.Mod => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase),
            InstanceContentImportKind.SaveArchive => SaveArchiveExtensions.Any(
                extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)),
            InstanceContentImportKind.ResourcePack or InstanceContentImportKind.ShaderPack =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
