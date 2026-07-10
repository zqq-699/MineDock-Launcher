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

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackExportServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ExportAsyncCreatesCurseForgeManifestAndOverrides()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var enabledModPath = CreateFile(instance.InstanceDirectory, "mods/enabled.jar", "enabled mod");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var resourcePackPath = CreateFile(instance.InstanceDirectory, "resourcepacks/pack.zip", "resource pack");
        var shaderPackPath = CreateFile(instance.InstanceDirectory, "shaderpacks/shader.zip", "shader pack");
        _ = CreateFile(instance.InstanceDirectory, "config/client.toml", "client config");
        var handler = new FingerprintMatchHandler(matchCount: 2);
        var service = CreateService(
            handler,
            "api-key",
            mods:
            [
                new LocalMod { FileName = "enabled.jar", FullPath = enabledModPath, IsEnabled = true },
                new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }
            ],
            resourcePacks:
            [
                new LocalResourcePack { FileName = "pack.zip", FullPath = resourcePackPath }
            ],
            shaderPacks:
            [
                new LocalShaderPack { FileName = "shader.zip", FullPath = shaderPackPath }
            ]);
        var outputPath = Path.Combine(TempRoot, "export.zip");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.CurseForge,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: false,
            IncludeResourcePacks: true,
            IncludeShaderPacks: true,
            IncludeConfig: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ManifestFileCount);
        Assert.Equal(2, result.OverrideFileCount);
        Assert.True(File.Exists(outputPath));
        using var archive = ZipFile.OpenRead(outputPath);
        using var manifest = ReadManifest(archive);
        var root = manifest.RootElement;
        Assert.Equal("minecraftModpack", root.GetProperty("manifestType").GetString());
        Assert.Equal("1.20.1", root.GetProperty("minecraft").GetProperty("version").GetString());
        Assert.Equal(
            "fabric-0.16.10",
            root.GetProperty("minecraft").GetProperty("modLoaders")[0].GetProperty("id").GetString());
        Assert.Equal(2, root.GetProperty("files").GetArrayLength());
        Assert.Equal(1000, root.GetProperty("files")[0].GetProperty("projectID").GetInt64());
        Assert.Equal(2000, root.GetProperty("files")[0].GetProperty("fileID").GetInt64());
        Assert.True(EntryExists(archive, "overrides/shaderpacks/shader.zip"));
        Assert.True(EntryExists(archive, "overrides/config/client.toml"));
        Assert.False(EntryExists(archive, "overrides/mods/enabled.jar"));
        Assert.False(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));
    }

    [Fact]
    public async Task ExportAsyncIncludesDisabledModsAsOverridesWhenRequested()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var enabledModPath = CreateFile(instance.InstanceDirectory, "mods/enabled.jar", "enabled mod");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 1),
            "api-key",
            mods:
            [
                new LocalMod { FileName = "enabled.jar", FullPath = enabledModPath, IsEnabled = true },
                new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }
            ]);
        var outputPath = Path.Combine(TempRoot, "disabled-mods.zip");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.CurseForge,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: true,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ManifestFileCount);
        Assert.Equal(1, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(outputPath);
        using var manifest = ReadManifest(archive);
        Assert.Equal(1, manifest.RootElement.GetProperty("files").GetArrayLength());
        Assert.True(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));
        Assert.False(EntryExists(archive, "overrides/mods/enabled.jar"));
    }

    [Fact]
    public async Task ExportAsyncAllowsEmptyAuthor()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null);
        var outputPath = Path.Combine(TempRoot, "empty-author.zip");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.CurseForge,
            "Pack",
            string.Empty,
            "1.0.0",
            outputPath,
            IncludeMods: false,
            IncludeDisabledMods: false,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(outputPath);
        using var manifest = ReadManifest(archive);
        Assert.Equal(string.Empty, manifest.RootElement.GetProperty("author").GetString());
    }

    [Fact]
    public async Task ExportAsyncDoesNotIncludeDisabledModsWhenModsAreExcluded()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            mods: [new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }]);
        var outputPath = Path.Combine(TempRoot, "mods-excluded.zip");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.CurseForge,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: false,
            IncludeDisabledMods: true,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ManifestFileCount);
        Assert.Equal(0, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(outputPath);
        using var manifest = ReadManifest(archive);
        Assert.Equal(0, manifest.RootElement.GetProperty("files").GetArrayLength());
        Assert.False(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));
    }

    [Fact]
    public async Task ExportAsyncCreatesModrinthIndexAndOverrides()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var enabledModPath = CreateFile(instance.InstanceDirectory, "mods/enabled.jar", "enabled mod");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var resourcePackPath = CreateFile(instance.InstanceDirectory, "resourcepacks/pack.zip", "resource pack");
        var shaderPackPath = CreateFile(instance.InstanceDirectory, "shaderpacks/shader.zip", "shader pack");
        _ = CreateFile(instance.InstanceDirectory, "config/client.toml", "client config");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            mods:
            [
                new LocalMod { FileName = "enabled.jar", FullPath = enabledModPath, IsEnabled = true },
                new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }
            ],
            resourcePacks:
            [
                new LocalResourcePack { FileName = "pack.zip", FullPath = resourcePackPath }
            ],
            shaderPacks:
            [
                new LocalShaderPack { FileName = "shader.zip", FullPath = shaderPackPath }
            ],
            modrinthHandler: new ModrinthVersionFilesHandler(matchCount: 2));
        var outputPath = Path.Combine(TempRoot, "export.mrpack");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.Modrinth,
            "Pack",
            "Ignored Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: false,
            IncludeResourcePacks: true,
            IncludeShaderPacks: true,
            IncludeConfig: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ManifestFileCount);
        Assert.Equal(2, result.OverrideFileCount);
        Assert.True(File.Exists(outputPath));
        using var archive = ZipFile.OpenRead(outputPath);
        using var index = ReadModrinthIndex(archive);
        var root = index.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("minecraft", root.GetProperty("game").GetString());
        Assert.Equal("1.0.0", root.GetProperty("versionId").GetString());
        Assert.Equal("Pack", root.GetProperty("name").GetString());
        Assert.Equal("1.20.1", root.GetProperty("dependencies").GetProperty("minecraft").GetString());
        Assert.Equal("0.16.10", root.GetProperty("dependencies").GetProperty("fabric-loader").GetString());
        Assert.False(root.TryGetProperty("author", out _));
        var files = root.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal("mods/enabled.jar", files[0].GetProperty("path").GetString());
        Assert.Equal("required", files[0].GetProperty("env").GetProperty("client").GetString());
        Assert.Equal("unsupported", files[0].GetProperty("env").GetProperty("server").GetString());
        Assert.StartsWith("https://cdn.modrinth.test/", files[0].GetProperty("downloads")[0].GetString());
        Assert.Equal(40, files[0].GetProperty("hashes").GetProperty("sha1").GetString()?.Length);
        Assert.Equal(128, files[0].GetProperty("hashes").GetProperty("sha512").GetString()?.Length);
        Assert.True(files[0].GetProperty("fileSize").GetInt64() > 0);
        Assert.True(EntryExists(archive, "overrides/shaderpacks/shader.zip"));
        Assert.True(EntryExists(archive, "overrides/config/client.toml"));
        Assert.False(EntryExists(archive, "overrides/mods/enabled.jar"));
        Assert.False(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));

        var packageService = new LocalModpackPackageService(new LauncherPathProvider(TempRoot, TempRoot));
        var recognition = await packageService.RecognizeAsync(outputPath);
        Assert.True(recognition.IsSuccess);
    }

    [Fact]
    public async Task ExportAsyncIncludesDisabledModsAsModrinthOverridesWhenRequested()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var enabledModPath = CreateFile(instance.InstanceDirectory, "mods/enabled.jar", "enabled mod");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            mods:
            [
                new LocalMod { FileName = "enabled.jar", FullPath = enabledModPath, IsEnabled = true },
                new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }
            ],
            modrinthHandler: new ModrinthVersionFilesHandler(matchCount: 1));
        var outputPath = Path.Combine(TempRoot, "disabled-mods.mrpack");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.Modrinth,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: true,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ManifestFileCount);
        Assert.Equal(1, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(outputPath);
        using var index = ReadModrinthIndex(archive);
        Assert.Equal(1, index.RootElement.GetProperty("files").GetArrayLength());
        Assert.True(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));
        Assert.False(EntryExists(archive, "overrides/mods/enabled.jar"));
    }

    [Fact]
    public async Task ExportAsyncDoesNotIncludeModrinthDisabledModsWhenModsAreExcluded()
    {
        var instance = CreateInstance(LoaderKind.Fabric, "0.16.10");
        var enabledModPath = CreateFile(instance.InstanceDirectory, "mods/enabled.jar", "enabled mod");
        var disabledModPath = CreateFile(instance.InstanceDirectory, "mods/disabled.jar.disabled", "disabled mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            mods:
            [
                new LocalMod { FileName = "enabled.jar", FullPath = enabledModPath, IsEnabled = true },
                new LocalMod { FileName = "disabled.jar.disabled", FullPath = disabledModPath, IsEnabled = false }
            ],
            modrinthHandler: new ModrinthVersionFilesHandler(matchCount: 1));
        var outputPath = Path.Combine(TempRoot, "mods-excluded.mrpack");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.Modrinth,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: false,
            IncludeDisabledMods: true,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ManifestFileCount);
        Assert.Equal(0, result.OverrideFileCount);
        using var archive = ZipFile.OpenRead(outputPath);
        using var index = ReadModrinthIndex(archive);
        Assert.Equal(0, index.RootElement.GetProperty("files").GetArrayLength());
        Assert.False(EntryExists(archive, "overrides/mods/enabled.jar"));
        Assert.False(EntryExists(archive, "overrides/mods/disabled.jar.disabled"));
    }

    [Fact]
    public async Task ExportAsyncCreatesVanillaModrinthDependencies()
    {
        var instance = CreateInstance(LoaderKind.Vanilla, loaderVersion: null);
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            modrinthHandler: new ModrinthVersionFilesHandler(matchCount: 0));
        var outputPath = Path.Combine(TempRoot, "vanilla.mrpack");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.Modrinth,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: false,
            IncludeDisabledMods: false,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(outputPath);
        using var index = ReadModrinthIndex(archive);
        var dependencies = index.RootElement.GetProperty("dependencies");
        Assert.Equal("1.20.1", dependencies.GetProperty("minecraft").GetString());
        Assert.Single(dependencies.EnumerateObject());
    }

    [Fact]
    public async Task ExportAsyncFailsWhenModrinthApiFailsAndDoesNotCreateArchive()
    {
        var instance = CreateInstance();
        var modPath = CreateFile(instance.InstanceDirectory, "mods/mod.jar", "mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 0),
            apiKey: null,
            mods: [new LocalMod { FileName = "mod.jar", FullPath = modPath, IsEnabled = true }],
            modrinthHandler: new ModrinthVersionFilesHandler(matchCount: 1, statusCode: HttpStatusCode.InternalServerError));
        var outputPath = Path.Combine(TempRoot, "api-failed.mrpack");

        var result = await service.ExportAsync(new ModpackExportRequest(
            instance,
            ModpackExportKind.Modrinth,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: false,
            IncludeResourcePacks: false,
            IncludeShaderPacks: false,
            IncludeConfig: false));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.ModrinthApiFailed, result.FailureReason);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportAsyncFailsWithoutCurseForgeApiKeyAndDoesNotCreateArchive()
    {
        var instance = CreateInstance();
        var modPath = CreateFile(instance.InstanceDirectory, "mods/mod.jar", "mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 1),
            apiKey: null,
            mods: [new LocalMod { FileName = "mod.jar", FullPath = modPath, IsEnabled = true }]);
        var outputPath = Path.Combine(TempRoot, "missing-key.zip");

        var result = await service.ExportAsync(CreateRequest(instance, outputPath));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.MissingCurseForgeApiKey, result.FailureReason);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportAsyncFailsWhenCurseForgeApiFailsAndDoesNotCreateArchive()
    {
        var instance = CreateInstance();
        var modPath = CreateFile(instance.InstanceDirectory, "mods/mod.jar", "mod");
        var service = CreateService(
            new FingerprintMatchHandler(matchCount: 1, statusCode: HttpStatusCode.InternalServerError),
            "api-key",
            mods: [new LocalMod { FileName = "mod.jar", FullPath = modPath, IsEnabled = true }]);
        var outputPath = Path.Combine(TempRoot, "api-failed.zip");

        var result = await service.ExportAsync(CreateRequest(instance, outputPath));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.CurseForgeApiFailed, result.FailureReason);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportAsyncFailsWhenLoaderVersionIsMissing()
    {
        var instance = CreateInstance(LoaderKind.Forge, loaderVersion: null);
        var service = CreateService(new FingerprintMatchHandler(matchCount: 0), "api-key");
        var outputPath = Path.Combine(TempRoot, "missing-loader.zip");

        var result = await service.ExportAsync(CreateRequest(instance, outputPath));

        Assert.False(result.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.MissingLoaderVersion, result.FailureReason);
        Assert.False(File.Exists(outputPath));

        var modrinthOutputPath = Path.Combine(TempRoot, "missing-loader.mrpack");

        var modrinthResult = await service.ExportAsync(CreateRequest(
            instance,
            modrinthOutputPath,
            ModpackExportKind.Modrinth));

        Assert.False(modrinthResult.IsSuccess);
        Assert.Equal(ModpackExportFailureReason.MissingLoaderVersion, modrinthResult.FailureReason);
        Assert.False(File.Exists(modrinthOutputPath));
    }

    private ModpackExportService CreateService(
        FingerprintMatchHandler handler,
        string? apiKey,
        IReadOnlyList<LocalMod>? mods = null,
        IReadOnlyList<LocalResourcePack>? resourcePacks = null,
        IReadOnlyList<LocalShaderPack>? shaderPacks = null,
        HttpMessageHandler? modrinthHandler = null)
    {
        return new ModpackExportService(
            new FakeModService(mods ?? []),
            new FakeResourcePackService(resourcePacks ?? []),
            new FakeShaderPackService(shaderPacks ?? []),
            new FakeCurseForgeApiKeyResolver(apiKey),
            new CurseForgeApiClient(new HttpClient(handler)),
            new ModrinthApiClient(new HttpClient(modrinthHandler ?? new ModrinthVersionFilesHandler(matchCount: 0))));
    }

    private GameInstance CreateInstance(
        LoaderKind loader = LoaderKind.Fabric,
        string? loaderVersion = "0.16.10")
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceDirectory);
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Export Test",
            MinecraftVersion = "1.20.1",
            Loader = loader,
            LoaderVersion = loaderVersion,
            InstanceDirectory = instanceDirectory
        };
    }

    private static ModpackExportRequest CreateRequest(
        GameInstance instance,
        string outputPath,
        ModpackExportKind kind = ModpackExportKind.CurseForge)
    {
        return new ModpackExportRequest(
            instance,
            kind,
            "Pack",
            "Author",
            "1.0.0",
            outputPath,
            IncludeMods: true,
            IncludeDisabledMods: false,
            IncludeResourcePacks: true,
            IncludeShaderPacks: true,
            IncludeConfig: true);
    }

    private static string CreateFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonDocument ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json missing");
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static JsonDocument ReadModrinthIndex(ZipArchive archive)
    {
        var entry = archive.GetEntry("modrinth.index.json")
            ?? throw new InvalidOperationException("modrinth.index.json missing");
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static bool EntryExists(ZipArchive archive, string entryName)
    {
        return archive.Entries.Any(entry => string.Equals(entry.FullName, entryName, StringComparison.Ordinal));
    }

    private sealed class FingerprintMatchHandler(int matchCount, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (statusCode is not HttpStatusCode.OK)
                return new HttpResponseMessage(statusCode);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var fingerprints = document.RootElement
                .GetProperty("fingerprints")
                .EnumerateArray()
                .Select(fingerprint => fingerprint.GetInt64())
                .ToArray();
            var matches = fingerprints
                .Take(matchCount)
                .Select((fingerprint, index) => $$"""
                {
                  "file": {
                    "id": {{2000 + index}},
                    "modId": {{1000 + index}},
                    "fileFingerprint": {{fingerprint}}
                  }
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                {
                  "data": {
                    "exactMatches": [
                      {{string.Join(",", matches)}}
                    ]
                  }
                }
                """)
            };
        }
    }

    private sealed class ModrinthVersionFilesHandler(
        int matchCount,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (statusCode is not HttpStatusCode.OK)
                return new HttpResponseMessage(statusCode);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var hashes = document.RootElement
                .GetProperty("hashes")
                .EnumerateArray()
                .Select(hash => hash.GetString())
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Cast<string>()
                .ToArray();
            var matches = hashes
                .Take(matchCount)
                .Select((hash, index) => $$"""
                "{{hash}}": {
                  "files": [
                    {
                      "hashes": {
                        "sha1": "{{hash}}",
                        "sha512": "{{new string((char)('a' + index), 128)}}"
                      },
                      "url": "https://cdn.modrinth.test/{{hash}}.jar",
                      "primary": true,
                      "size": {{100 + index}}
                    }
                  ]
                }
                """);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                {
                  {{string.Join(",", matches)}}
                }
                """)
            };
        }
    }

    private sealed class FakeCurseForgeApiKeyResolver(string? apiKey) : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(apiKey);
        }
    }

    private sealed class FakeModService(IReadOnlyList<LocalMod> mods) : IModService
    {
        public Task<IReadOnlyList<LocalMod>> GetModsAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(mods);
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeResourcePackService(IReadOnlyList<LocalResourcePack> resourcePacks)
        : ILocalResourcePackService
    {
        public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(resourcePacks);
        }

        public Task<LocalResourcePackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalResourcePack resourcePack, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(IEnumerable<LocalResourcePack> resourcePacks, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeShaderPackService(IReadOnlyList<LocalShaderPack> shaderPacks)
        : ILocalShaderPackService
    {
        public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(shaderPacks);
        }

        public Task<LocalShaderPackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
