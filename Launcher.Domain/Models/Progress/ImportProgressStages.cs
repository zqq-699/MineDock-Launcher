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

namespace Launcher.Domain.Models;

public static class ImportProgressStages
{
    public const string PreparingArchive = "Import.PreparingArchive";
    public const string ParsingManifest = "Import.ParsingManifest";
    public const string ResolvingPackFiles = "Import.ResolvingPackFiles";
    public const string CreatingInstance = "Import.CreatingInstance";
    public const string InstallingMinecraftBase = "Import.InstallingMinecraftBase";
    public const string InstallingLoader = "Import.InstallingLoader";
    public const string DownloadingPackFiles = "Import.DownloadingPackFiles";
    public const string CopyingOverrides = "Import.CopyingOverrides";
    public const string CleaningUp = "Import.CleaningUp";
}
