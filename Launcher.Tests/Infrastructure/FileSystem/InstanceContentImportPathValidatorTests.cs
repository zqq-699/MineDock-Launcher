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

using Launcher.Application.Services;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class InstanceContentImportPathValidatorTests
{
    [Theory]
    [InlineData(InstanceContentImportKind.Mod, "sample.jar")]
    [InlineData(InstanceContentImportKind.SaveArchive, "world.tar.gz")]
    [InlineData(InstanceContentImportKind.ResourcePack, "resources.zip")]
    [InlineData(InstanceContentImportKind.ShaderPack, "shaders.zip")]
    public void ExistingSupportedFileIsAccepted(InstanceContentImportKind kind, string fileName)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, fileName);
        File.WriteAllText(path, "test");

        var result = new InstanceContentImportPathValidator().Validate([path], kind);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DirectoryIsReportedSeparatelyFromInvalidFile()
    {
        using var directory = new TemporaryDirectory();

        var result = new InstanceContentImportPathValidator().Validate(
            [directory.Path],
            InstanceContentImportKind.Mod);

        Assert.Equal(InstanceContentImportPathFailure.DirectoryNotSupported, result.Failure);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "launcher-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
