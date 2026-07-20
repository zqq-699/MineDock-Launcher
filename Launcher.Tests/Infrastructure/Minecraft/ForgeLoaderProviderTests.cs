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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ForgeLoaderProviderTests : TestTempDirectory
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ForgeLoaderProviderPassesSelectedJavaPathToInstallerRunner()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var expectedJavaPath = Path.Combine(TempRoot, "Selected Java", "bin", "java.exe");
        string? receivedJavaPath = null;
        var javaRuntimeResolver = new FixedJavaRuntimeResolver(expectedJavaPath);
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, javaPath, _) =>
            {
                receivedJavaPath = javaPath;
                return CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20");
            }),
            javaRuntimeResolver: javaRuntimeResolver);

        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        Assert.Equal(expectedJavaPath, receivedJavaPath);
        Assert.Equal(minecraftDirectory, javaRuntimeResolver.LastRequest?.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.Official, javaRuntimeResolver.LastRequest?.DownloadSourcePreference);
        Assert.Equal(LoaderKind.Forge, javaRuntimeResolver.LastRequest?.Loader);
        Assert.Equal("47.4.20", javaRuntimeResolver.LastRequest?.LoaderVersion);
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotStartInstallerWhenJavaSelectionFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var runnerStarted = false;
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            javaRuntimeResolver: new FailingJavaRuntimeResolver());

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, exception.Reason);
        Assert.False(runnerStarted);
    }

    [Fact]
    public async Task ForgeInstallerRunnerCancellationTerminatesEntireProcessTree()
    {
        Directory.CreateDirectory(TempRoot);
        var scriptPath = Path.Combine(TempRoot, "long-running-forge-installer.ps1");
        var parentProcessIdPath = Path.Combine(TempRoot, "forge-parent.pid");
        var childProcessIdPath = Path.Combine(TempRoot, "forge-child.pid");
        await File.WriteAllTextAsync(
            scriptPath,
            $$"""
            $PID | Set-Content -LiteralPath '{{parentProcessIdPath}}'
            $child = Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/s', '/c', 'ping 127.0.0.1 -n 300 > nul' -PassThru
            $child.Id | Set-Content -LiteralPath '{{childProcessIdPath}}'
            Wait-Process -Id $child.Id
            """);

        Process? startedProcess = null;
        int? childProcessId = null;
        var runner = new ForgeInstallerRunner(_ =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startedProcess = Process.Start(startInfo);
            return startedProcess;
        });
        using var cancellation = new CancellationTokenSource();

        try
        {
            var runTask = runner.RunInstallerAsync(
                Path.Combine(TempRoot, "ignored-java.exe"),
                Path.Combine(TempRoot, "ignored-installer.jar"),
                TempRoot,
                cancellation.Token);
            var parentProcessId = await WaitForProcessIdAsync(parentProcessIdPath);
            childProcessId = await WaitForProcessIdAsync(childProcessIdPath);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
            await WaitUntilAsync(
                () => HasExited(parentProcessId) && HasExited(childProcessId.Value),
                ProcessTimeout,
                "The canceled Forge installer process tree did not exit.");
        }
        finally
        {
            TryKillProcessTree(startedProcess);
            TryKillProcess(childProcessId);
        }
    }

    [Fact]
    public async Task ForgeInstallerRunnerDoesNotStartWhenAlreadyCanceled()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunInstallerAsync(
            Path.Combine(TempRoot, "ignored-java.exe"),
            Path.Combine(TempRoot, "ignored-installer.jar"),
            TempRoot,
            cancellation.Token));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeInstallerRunnerUsesExactJavaPathAndArgumentList()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var javaPath = Path.Combine(TempRoot, "Java Runtime", "bin", "java.exe");
        var installerPath = Path.Combine(TempRoot, "Forge Installer", "forge installer.jar");
        var gameDirectory = Path.Combine(TempRoot, "Game Directory");
        var runner = new ForgeInstallerRunner(startInfo =>
        {
            capturedStartInfo = startInfo;
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add("exit 0");
            return Process.Start(processStartInfo);
        });

        await runner.RunInstallerAsync(javaPath, installerPath, gameDirectory, CancellationToken.None);

        Assert.NotNull(capturedStartInfo);
        Assert.Equal(javaPath, capturedStartInfo.FileName);
        Assert.Equal(["-jar", installerPath, "--installClient", gameDirectory], capturedStartInfo.ArgumentList);
    }

    [Fact]
    public async Task ForgeInstallerRunnerUsesServerInstallArgument()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var javaPath = Path.Combine(TempRoot, "Java Runtime", "bin", "java.exe");
        var installerPath = Path.Combine(TempRoot, "Forge Installer", "forge installer.jar");
        var serverDirectory = Path.Combine(TempRoot, "Server Directory");
        var runner = new ForgeInstallerRunner(startInfo =>
        {
            capturedStartInfo = startInfo;
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add("exit 0");
            return Process.Start(processStartInfo);
        });

        await runner.RunServerInstallerAsync(javaPath, installerPath, serverDirectory, CancellationToken.None);

        Assert.NotNull(capturedStartInfo);
        Assert.Equal(javaPath, capturedStartInfo.FileName);
        Assert.Equal(["-jar", installerPath, "--installServer", serverDirectory], capturedStartInfo.ArgumentList);
    }

    [Fact]
    public async Task ForgeInstallerRunnerRejectsMissingJavaPathWithoutStartingProcess()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunInstallerAsync(
            string.Empty,
            Path.Combine(TempRoot, "installer.jar"),
            TempRoot,
            CancellationToken.None));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeInstallerRunnerRejectsPathJavaFallbackWithoutStartingProcess()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunInstallerAsync(
            "java",
            Path.Combine(TempRoot, "installer.jar"),
            TempRoot,
            CancellationToken.None));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeLoaderProviderParsesVersionsFromOfficialCatalog()
    {
        var provider = CreateProvider();

        var versions = await provider.GetLoaderVersionsAsync("1.20.1");

        Assert.Equal(["47.4.20", "47.4.10"], versions.Select(version => version.Version));
        Assert.All(versions, version => Assert.True(version.IsStable));
    }

    [Fact]
    public void MergeFlattenedVersionNormalizesLegacyForgeMinecraftArguments()
    {
        var baseVersion = new JsonObject
        {
            ["id"] = "1.12.2",
            ["minecraftArguments"] = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --versionType ${version_type}"
        };
        var derivedVersion = new JsonObject
        {
            ["id"] = "forge-1.12.2-14.23.5.2860",
            ["inheritsFrom"] = "1.12.2",
            ["minecraftArguments"] = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge"
        };

        var merged = VersionJsonMergeHelper.MergeFlattenedVersion(baseVersion, derivedVersion, "RLCraft", "1.12.2");

        var arguments = merged["minecraftArguments"]!.GetValue<string>();
        Assert.Equal(1, CountArgument(arguments, "--gameDir"));
        Assert.Equal(1, CountArgument(arguments, "--assetsDir"));
        Assert.Equal(1, CountArgument(arguments, "--accessToken"));
        Assert.Contains("--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker", arguments);
        Assert.Contains("--versionType Forge", arguments);
        Assert.DoesNotContain("--versionType ${version_type}", arguments);
    }

    [Fact]
    public async Task LegacyForgeInstallerPlanUsesVersionInfoAndEmbeddedUniversalPayload()
    {
        var installerPath = Path.Combine(TempRoot, "legacy-forge-installer.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllBytesAsync(installerPath, CreateLegacyForgeInstallerBytes());
        var service = new LoaderInstallerArtifactService(new HttpClient(new ForgeHttpHandler()));

        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);

        var forge = Assert.Single(
            plan.RuntimeLibraries,
            library => library.Artifact.LibraryName == "net.minecraftforge:forge:1.10.2-12.18.3.2511");
        Assert.Equal(
            "forge-1.10.2-12.18.3.2511-universal.jar",
            forge.EmbeddedEntryName);
        Assert.Contains(
            plan.RuntimeLibraries,
            library => library.Artifact.LibraryName == "net.minecraft:launchwrapper:1.12");
    }

    [Fact]
    public async Task ForgeLoaderProviderInstallCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var finalInstaller = new RecordingFinalVersionInstaller();

        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            return CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
        }), finalInstaller);

        var finalVersionName = await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var versionDirectories = Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal("1.20.1-forge-47.4.20", finalVersionName);
        Assert.Equal(["1.20.1", "1.20.1-forge-47.4.20"], versionDirectories);
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "forge-1.20.1-47.4.20")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "1.20.1-forge-47.4.20.jar")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "win_args.txt")));
        Assert.True(File.Exists(Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            "1.20.1-47.4.20",
            "forge-1.20.1-47.4.20-client.jar")));
        Assert.True(File.Exists(GetGeneratedLibraryPath(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar")));
        Assert.True(File.Exists(GetGeneratedLibraryPath(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar")));
        Assert.True(File.Exists(GetGeneratedLibraryPath(
            minecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar")));
        Assert.False(File.Exists(Path.Combine(minecraftDirectory, "launcher_profiles.json")));
        Assert.Equal("1.20.1-forge-47.4.20", finalInstaller.LastVersionName);
        Assert.NotNull(finalInstaller.LastPath);
        Assert.StartsWith(Path.Combine(TempRoot, "launcher-forge"), finalInstaller.LastPath!.Versions, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(minecraftDirectory, "libraries"), finalInstaller.LastPath.Library);
        Assert.Equal(Path.Combine(minecraftDirectory, "assets"), finalInstaller.LastPath.Assets);
        Assert.Equal(Path.Combine(minecraftDirectory, "resources"), finalInstaller.LastPath.Resource);
        Assert.Equal(Path.Combine(minecraftDirectory, "runtime"), finalInstaller.LastPath.Runtime);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "1.20.1-forge-47.4.20.json")));
        Assert.Equal("1.20.1-forge-47.4.20", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1-forge-47.4.20", json.RootElement.GetProperty("jar").GetString());
        Assert.False(json.RootElement.TryGetProperty("inheritsFrom", out _));
        Assert.Equal("1.20.1", json.RootElement.GetProperty("launcher").GetProperty("minecraftVersion").GetString());
        Assert.False(json.RootElement.GetProperty("launcher").TryGetProperty("forgeProcessorArtifacts", out _));
    }

    [Fact]
    public async Task ForgeIntegrityRepairUsesInstallerManifestWithoutLegacyMarkerAndPreservesUserContent()
    {
        const string versionName = "Better MC [FORGE] BMC4";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var httpClient = new HttpClient(new ForgeHttpHandler());
        var provider = new ForgeLoaderProvider(
            httpClient,
            new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
                CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20")),
            new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: new FixedJavaRuntimeResolver());
        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            versionName,
            "47.4.20",
            progress: null);
        await EnsureUnverifiedVersionLibrariesExistAsync(minecraftDirectory, versionName);
        var versionJsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        versionJson["launcher"]!.AsObject()["forgeProcessorArtifacts"] = new JsonObject
        {
            ["schemaVersion"] = 2
        };
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString());

        var missingRelativePaths = new[]
        {
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-client.jar"
        };
        foreach (var relativePath in missingRelativePaths)
            File.Delete(GetGeneratedLibraryPath(minecraftDirectory, relativePath));

        var userFiles = new Dictionary<string, string>
        {
            [Path.Combine(minecraftDirectory, "mods", "keep.jar")] = "mod",
            [Path.Combine(minecraftDirectory, "config", "keep.toml")] = "config",
            [Path.Combine(minecraftDirectory, "saves", "World", "level.dat")] = "save"
        };
        foreach (var userFile in userFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userFile.Key)!);
            await File.WriteAllTextAsync(userFile.Key, userFile.Value);
        }

        var service = new GameFileIntegrityService(
            httpClient,
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());
        var progressReports = new ConcurrentQueue<LauncherProgress>();
        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(
                minecraftDirectory,
                versionName,
                Path.Combine(minecraftDirectory, "versions", versionName))
            {
                LoaderIdentity = new GameFileLoaderIdentity(
                    LoaderKind.Forge,
                    "1.20.1",
                    "47.4.20")
            },
            new GameFileRepairOptions(AllowRepair: true),
            new InlineProgress(progressReports));

        Assert.True(
            result.LaunchAllowed,
            string.Join(Environment.NewLine, result.Failures.Select(failure =>
                $"{failure.Category}: {failure.Reason} {failure.TargetPath} {failure.Source}")));
        Assert.True(result.RepairedCount >= missingRelativePaths.Length);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.True(File.Exists(GetGeneratedLibraryPath(minecraftDirectory, relativePath))));
        foreach (var userFile in userFiles)
            Assert.Equal(userFile.Value, await File.ReadAllTextAsync(userFile.Key));

        var manifest = await LoaderArtifactManifestStore.ReadAsync(
            Path.Combine(minecraftDirectory, "versions", versionName),
            new GameFileLoaderIdentity(LoaderKind.Forge, "1.20.1", "47.4.20"),
            CancellationToken.None);
        Assert.True(manifest.IsValid);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.Contains(
                manifest.Manifest!.Artifacts,
                artifact => artifact.RelativePath == $"libraries/{relativePath}"));
        using var repairedVersionJson = JsonDocument.Parse(await File.ReadAllTextAsync(versionJsonPath));
        Assert.False(
            repairedVersionJson.RootElement.GetProperty("launcher").TryGetProperty(
                "forgeProcessorArtifacts",
                out _));

        var visibleProgress = progressReports
            .Where(report => report.DownloadSpeedTelemetry is null)
            .ToArray();
        Assert.DoesNotContain(
            visibleProgress,
            report => report.Stage.StartsWith("Install.", StringComparison.Ordinal));
        var expectedStages = new[]
        {
            LaunchProgressStages.RepairingLoaderInstaller,
            LaunchProgressStages.CheckingJava,
            LaunchProgressStages.RunningLoaderInstaller,
            LaunchProgressStages.FinalizingLoaderVersion,
            LaunchProgressStages.PublishingLoaderArtifacts,
            LaunchProgressStages.RevalidatingFiles
        };
        var previousIndex = -1;
        foreach (var stage in expectedStages)
        {
            var index = Array.FindIndex(visibleProgress, report => report.Stage == stage);
            Assert.True(index > previousIndex, $"Launch progress stage {stage} was missing or out of order.");
            previousIndex = index;
        }
        var percents = visibleProgress
            .Where(report => report.Percent is not null)
            .Select(report => report.Percent!.Value)
            .ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.Equal(90, visibleProgress.Last(report => report.Stage == LaunchProgressStages.RevalidatingFiles).Percent);
    }

    [Fact]
    public async Task ForgeLoaderProviderStagedInstallPublishesSharedRuntimeDirectly()
    {
        var sharedMinecraftDirectory = Path.Combine(TempRoot, "shared", ".minecraft");
        var outputMinecraftDirectory = Path.Combine(TempRoot, "output", ".minecraft");
        await CreateVanillaVersionAsync(sharedMinecraftDirectory, "1.20.1");
        CreateGeneratedForgeLibrary(sharedMinecraftDirectory, "1.20.1-47.4.20");
        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
            CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20")));

        var finalVersionName = await provider.InstallStagedAsync(
            "1.20.1",
            outputMinecraftDirectory,
            sharedMinecraftDirectory,
            "Imported Forge Pack",
            "47.4.20",
            progress: null,
            LauncherDefaults.DefaultDownloadSourcePreference,
            CancellationToken.None,
            downloadSpeedLimitMbPerSecond: 0);

        Assert.Equal("Imported Forge Pack", finalVersionName);
        Assert.True(File.Exists(GetGeneratedLibraryPath(
            sharedMinecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar")));
        Assert.False(File.Exists(GetGeneratedLibraryPath(
            outputMinecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar")));
    }

    [Fact]
    public async Task ForgeLoaderProviderRunsInstallerInTempSandboxInsteadOfMainMinecraftDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        string? installerGameDirectory = null;
        var sawLauncherProfile = false;

        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            installerGameDirectory = gameDirectory;
            sawLauncherProfile = File.Exists(Path.Combine(gameDirectory, "launcher_profiles.json"));
            return CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
        }));

        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        Assert.NotNull(installerGameDirectory);
        Assert.NotEqual(
            Path.GetFullPath(minecraftDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(installerGameDirectory!).TrimEnd(Path.DirectorySeparatorChar));
        Assert.StartsWith(Path.Combine(TempRoot, "launcher-forge"), installerGameDirectory!, StringComparison.OrdinalIgnoreCase);
        Assert.True(sawLauncherProfile);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "forge-1.20.1-47.4.20")));
    }

    [Fact]
    public async Task ForgeLoaderProviderInstallCleansCreatedVersionDirectoriesWhenInstallerFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");

        var provider = CreateProvider(new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
        {
            await CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.1")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "forge-1.20.1-47.4.20")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.1-forge-47.4.20")));
    }

    [Fact]
    public async Task ForgeLoaderProviderRejectsSuccessfulInstallerWithMissingProcessorOutput()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var provider = CreateProvider(new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
        {
            await CreateSandboxForgeInstallAsync(
                gameDirectory,
                "forge-1.20.1-47.4.20",
                "1.20.1",
                "1.20.1-47.4.20");
            File.Delete(GetGeneratedLibraryPath(
                gameDirectory,
                "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar"));
        }));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.Contains("processor output", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.1-forge-47.4.20")));
    }

    private static int CountArgument(string arguments, string argument)
    {
        return arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(token => string.Equals(token, argument, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> WaitForProcessIdAsync(string path)
    {
        int? processId = null;
        await WaitUntilAsync(
            () => (processId = TryReadProcessId(path)) is not null,
            ProcessTimeout,
            $"The process ID file was not created: {path}");
        return processId!.Value;
    }

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var processId)
                ? processId
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKillProcessTree(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryKillProcess(int? processId)
    {
        if (processId is null)
            return;

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(25);
        }
    }

    private ForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        ILoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null)
    {
        return new ForgeLoaderProvider(
            new HttpClient(new ForgeHttpHandler()),
            runner ?? new NoOpForgeInstallerRunner(),
            finalVersionInstaller ?? new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: javaRuntimeResolver ?? new FixedJavaRuntimeResolver());
    }

    private static async Task CreateSandboxForgeInstallAsync(
        string minecraftDirectory,
        string versionName,
        string inheritsFrom,
        string combinedForgeVersion)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, inheritsFrom);
        CreateForgeDerivedVersion(minecraftDirectory, versionName, inheritsFrom, combinedForgeVersion);
        CreateGeneratedForgeLibrary(minecraftDirectory, combinedForgeVersion);
    }

    private static async Task CreateVanillaVersionAsync(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            $$"""
            {
              "id": "{{versionName}}",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [
                { "name": "com.mojang:patchy:2.2.10" }
              ],
              "arguments": {
                "game": [ "--username", "${auth_player_name}" ],
                "jvm": [ "-Djava.library.path=${natives_directory}" ]
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.jar"), "base jar");
    }

    private static void CreateForgeDerivedVersion(string minecraftDirectory, string versionName, string inheritsFrom, string combinedForgeVersion)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "net.minecraftforge.client.loading.ClientModLoader",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.minecraftforge:forge:{combinedForgeVersion}" }
            }
        };

        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget forge_client");
    }

    private static void CreateGeneratedForgeLibrary(string minecraftDirectory, string combinedForgeVersion)
    {
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "minecraft extra");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "minecraft srg");
        WriteGeneratedLibrary(
            minecraftDirectory,
            $"net/minecraftforge/forge/{combinedForgeVersion}/forge-{combinedForgeVersion}-client.jar",
            "patched forge client");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
            "forge runtime");
    }

    private static void WriteGeneratedLibrary(string minecraftDirectory, string relativePath, string content)
    {
        var path = GetGeneratedLibraryPath(minecraftDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class InlineProgress(ConcurrentQueue<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Enqueue(value);
    }

    private static async Task EnsureUnverifiedVersionLibrariesExistAsync(
        string minecraftDirectory,
        string versionName)
    {
        var versionPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var version = JsonNode.Parse(await File.ReadAllTextAsync(versionPath))!.AsObject();
        if (version["libraries"] is not JsonArray libraries)
            return;
        foreach (var library in libraries.OfType<JsonObject>())
        {
            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
            {
                if (MinecraftFileIntegrity.IsSha1(artifact.Sha1))
                    continue;
                var path = GetGeneratedLibraryPath(minecraftDirectory, artifact.RelativePath);
                if (File.Exists(path))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "standard library");
            }
        }
    }

    private static string GetGeneratedLibraryPath(string minecraftDirectory, string relativePath) =>
        Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] CreateModernForgeInstallerBytes()
    {
        static string Sha1(string content) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    $$"""
                {
                  "spec": 1,
                  "minecraft": "1.20.1",
                  "data": {
                    "MC_EXTRA": { "client": "[net.minecraft:client:1.20.1-20230612.114412:extra]" },
                    "MC_EXTRA_SHA": { "client": "'{{Sha1("minecraft extra")}}'" },
                    "MC_SRG": { "client": "[net.minecraft:client:1.20.1-20230612.114412:srg]" },
                    "PATCHED": { "client": "[net.minecraftforge:forge:1.20.1-47.4.20:client]" },
                    "PATCHED_SHA": { "client": "'{{Sha1("patched forge client")}}'" }
                  },
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "{{Sha1("forge runtime")}}",
                          "size": 13
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "sides": ["client"], "args": ["--extra", "{MC_EXTRA}"], "outputs": { "{MC_EXTRA}": "{MC_EXTRA_SHA}" } },
                    { "args": ["--output", "{MC_SRG}"] },
                    { "args": ["--output", "{PATCHED}"], "outputs": { "{PATCHED}": "{PATCHED_SHA}" } }
                  ]
                }
                """);
            }
            var runtimeEntry = archive.CreateEntry(
                "maven/net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar");
            using (var runtimeWriter = new StreamWriter(runtimeEntry.Open()))
            {
                runtimeWriter.Write("forge runtime");
            }
            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "9de99c8b24ff448def492a91d4aa09e29511b66c",
                          "size": 13
                        }
                      }
                    }
                  ]
                }
                """);
        }
        return stream.ToArray();
    }

    private static byte[] CreateLegacyForgeInstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    """
                    {
                      "install": {
                        "profileName": "forge",
                        "target": "1.10.2-forge1.10.2-12.18.3.2511",
                        "path": "net.minecraftforge:forge:1.10.2-12.18.3.2511",
                        "version": "forge 1.10.2-12.18.3.2511",
                        "filePath": "forge-1.10.2-12.18.3.2511-universal.jar",
                        "minecraft": "1.10.2"
                      },
                      "versionInfo": {
                        "id": "1.10.2-forge1.10.2-12.18.3.2511",
                        "type": "release",
                        "minecraftArguments": "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "inheritsFrom": "1.10.2",
                        "jar": "1.10.2",
                        "libraries": [
                          { "name": "net.minecraftforge:forge:1.10.2-12.18.3.2511", "url": "https://maven.minecraftforge.net/" },
                          { "name": "net.minecraft:launchwrapper:1.12" }
                        ]
                      }
                    }
                    """);
            }

            var forgeJarEntry = archive.CreateEntry("forge-1.10.2-12.18.3.2511-universal.jar");
            using var forgeJarWriter = new StreamWriter(forgeJarEntry.Open());
            forgeJarWriter.Write("legacy forge universal jar");
        }

        return stream.ToArray();
    }

    private sealed class NoOpForgeInstallerRunner : IForgeInstallerRunner
    {
        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpFinalVersionInstaller : IFinalVersionInstaller
    {
        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFinalVersionInstaller : IFinalVersionInstaller
    {
        public string? LastVersionName { get; private set; }
        public MinecraftPath? LastPath { get; private set; }

        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastVersionName = versionName;
            return Task.CompletedTask;
        }

        public Task InstallAsync(
            MinecraftPath path,
            string versionName,
            MinecraftDownloadOperationContext operationContext,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastPath = path;
            LastVersionName = versionName;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedJavaRuntimeResolver(string? executablePath = null) : ILoaderInstallerJavaRuntimeResolver
    {
        public LoaderInstallerJavaRuntimeRequest? LastRequest { get; private set; }

        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var path = executablePath ?? Path.Combine("C:\\Program Files", "Launcher Java", "bin", "java.exe");
            return Task.FromResult(new JavaRuntimeInfo(
                "Launcher Java 21",
                "21.0.2",
                21,
                "x64",
                path,
                Path.GetDirectoryName(Path.GetDirectoryName(path))!,
                "Test"));
        }
    }

    private sealed class FailingJavaRuntimeResolver : ILoaderInstallerJavaRuntimeResolver
    {
        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new JavaRuntimeSelectionException(
                "No compatible Java runtime is available.",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                17);
        }
    }

    private sealed class LegacyFallbackFinalVersionInstaller : IFinalVersionInstaller
    {
        public List<string> InstalledVersionNames { get; } = [];

        public async Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstalledVersionNames.Add(versionName);
            if (string.Equals(versionName, "1.10.2", StringComparison.OrdinalIgnoreCase))
                await CreateVanillaVersionAsync(gameDirectory, versionName);
        }
    }

    private sealed class ScriptedForgeInstallerRunner : IForgeInstallerRunner
    {
        private readonly Func<string, string, string, Task> callback;

        public ScriptedForgeInstallerRunner(Func<string, string, string, Task> callback)
        {
            this.callback = callback;
        }

        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return callback(minecraftDirectory, javaCommand, installerJarPath);
        }
    }

    private sealed class ForgeHttpHandler : HttpMessageHandler
    {
        private readonly bool include1201Html;
        private readonly bool include1102Html;
        private readonly byte[]? legacyInstallerBytes;
        private readonly string promotionsJson;

        public ForgeHttpHandler(
            bool include1201Html = true,
            bool include1102Html = false,
            string? promotionsJson = null,
            byte[]? legacyInstallerBytes = null)
        {
            this.include1201Html = include1201Html;
            this.include1102Html = include1102Html;
            this.legacyInstallerBytes = legacyInstallerBytes;
            this.promotionsJson = promotionsJson ?? """
                {
                  "promos": {
                    "1.20.1-latest": "47.4.20",
                    "1.20.1-recommended": "47.4.10"
                  }
                }
                """;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.minecraftforge.net/", StringComparison.OrdinalIgnoreCase);
            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json")
            {
                return Task.FromResult(CreateJsonResponse(request, promotionsJson));
            }

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.20.1.html")
            {
                if (!include1201Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar">47.4.20</a>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar">47.4.10</a>
                      </body>
                    </html>
                    """));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.10.2.html")
            {
                if (!include1102Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar">12.18.3.2511</a>
                      </body>
                    </html>
                    """));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request, legacyInstallerBytes));

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage request, string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }

        private static HttpResponseMessage CreateHtmlResponse(HttpRequestMessage request, string html)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request)
        {
            return CreateBinaryResponse(request, CreateModernForgeInstallerBytes());
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, byte[]? content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content ?? "forge installer bytes"u8.ToArray())
            };
        }
    }
}
