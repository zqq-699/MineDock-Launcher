/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LauncherBackgroundImageCatalog : ILauncherBackgroundImageCatalog
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    };

    public LauncherBackgroundImageCatalog(LauncherPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        DirectoryPath = Path.Combine(pathProvider.DefaultDataDirectory, "images");
    }

    public string DirectoryPath { get; }

    public string EnsureDirectoryExists()
    {
        Directory.CreateDirectory(DirectoryPath);
        return DirectoryPath;
    }

    public IReadOnlyList<string> GetCandidatePaths()
    {
        var directory = EnsureDirectoryExists();
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ClearImages()
    {
        foreach (var imagePath in GetCandidatePaths())
            File.Delete(imagePath);
    }
}
