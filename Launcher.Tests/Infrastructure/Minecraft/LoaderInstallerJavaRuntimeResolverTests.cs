/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using System.Net;
using System.Text;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LoaderInstallerJavaRuntimeResolverTests
{
    [Fact]
    public async Task RequirementResolverUsesAuthoritativeJavaVersionFromMetadata()
    {
        using var httpClient = new HttpClient(new VersionMetadataHandler("26.2", 25));
        var resolver = new LoaderInstallerJavaRequirementResolver(httpClient);

        var requirement = await resolver.ResolveRequirementAsync(
            CreateRequest("26.2", "forge-instance"));

        Assert.Equal(25, requirement.RecommendedMajorVersion);
    }

    [Fact]
    public async Task NewInstallUsesCompatibleGlobalManualJavaWithoutProvisioning()
    {
        const string javaPath = @"C:\Program Files\Launcher Java\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(javaPath, 21)
        };
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Wrong\.minecraft",
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = javaPath
        };
        var provisioner = new RecordingProvisioner();
        var resolver = CreateResolver(settings, [], discovery, requiredMajorVersion: 21, provisioner);

        var runtime = await resolver.ResolveAsync(CreateRequest("1.20.5", "forge-instance"));

        Assert.Equal(javaPath, runtime.ExecutablePath);
        Assert.Equal(javaPath, discovery.LastProbedPath);
        Assert.Equal(0, provisioner.CallCount);
    }

    [Fact]
    public async Task IncompatibleManualJavaFallsBackToAnotherDiscoveredRuntime()
    {
        const string manualPath = @"C:\Java\jdk-8\bin\java.exe";
        const string expectedPath = @"C:\Java\jdk-17\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(manualPath, 8),
            Runtimes =
            [
                CreateRuntime(manualPath, 8),
                CreateRuntime(expectedPath, 17)
            ]
        };
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = manualPath
        };
        var provisioner = new RecordingProvisioner();
        var resolver = CreateResolver(settings, [], discovery, requiredMajorVersion: 17, provisioner);

        var runtime = await resolver.ResolveAsync(CreateRequest("1.20.1", "forge-instance"));

        Assert.Equal(expectedPath, runtime.ExecutablePath);
        Assert.Equal(0, provisioner.CallCount);
    }

    [Fact]
    public async Task LoaderUpperBoundRejectsManualJavaAndUsesCompatibleDiscoveredRuntime()
    {
        const string manualPath = @"C:\Java\jdk-17\bin\java.exe";
        const string expectedPath = @"C:\Java\jdk-8\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(manualPath, 17),
            Runtimes =
            [
                CreateRuntime(manualPath, 17),
                CreateRuntime(expectedPath, 8)
            ]
        };
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = manualPath
        };
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.12.2",
            LoaderKind.Forge,
            "14.23.5.2860",
            metadataMajorVersion: 8);
        var provisioner = new RecordingProvisioner();
        var resolver = CreateResolver(settings, [], discovery, requirement, provisioner);

        var runtime = await resolver.ResolveAsync(CreateRequest("1.12.2", "forge-instance"));

        Assert.Equal(expectedPath, runtime.ExecutablePath);
        Assert.Equal(0, provisioner.CallCount);
    }

    [Fact]
    public async Task MissingCompatibleJavaIsProvisionedThenRediscovered()
    {
        const string provisionedPath = @"C:\Games\.minecraft\runtime\java-runtime-gamma\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            Runtimes = [CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21)]
        };
        var progressReports = new List<LauncherProgress>();
        var request = CreateRequest("26.2", "forge-instance", new InlineProgress(progressReports));
        var provisioner = new RecordingProvisioner(_ =>
            discovery.Runtimes = [CreateRuntime(provisionedPath, 25)]);
        var resolver = CreateResolver(
            new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Auto },
            [],
            discovery,
            requiredMajorVersion: 25,
            provisioner);

        var runtime = await resolver.ResolveAsync(request);

        Assert.Equal(provisionedPath, runtime.ExecutablePath);
        Assert.Equal(1, provisioner.CallCount);
        Assert.Same(request, provisioner.LastRequest);
        Assert.Contains(progressReports, report => report.Stage == InstallProgressStages.DownloadingJava);
        Assert.Equal(request.MinecraftDirectory, discovery.LastMinecraftDirectory);
    }

    [Fact]
    public async Task ExistingInstanceUsesItsCompatiblePerInstanceJavaSelection()
    {
        const string instancePath = @"C:\Java\instance\bin\java.exe";
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ManualRuntime = CreateRuntime(instancePath, 21)
        };
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = @"C:\Java\global\bin\java.exe"
        };
        var storedInstance = new GameInstance
        {
            Name = "Repair Target",
            MinecraftVersion = "1.20.5",
            VersionName = "repair-target",
            JavaSettingsMode = LaunchSettingsMode.PerInstance,
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = instancePath
        };
        var provisioner = new RecordingProvisioner();
        var resolver = CreateResolver(settings, [storedInstance], discovery, requiredMajorVersion: 21, provisioner);

        var runtime = await resolver.ResolveAsync(CreateRequest("1.20.5", "repair-target"));

        Assert.Equal(instancePath, runtime.ExecutablePath);
        Assert.Equal(instancePath, discovery.LastProbedPath);
        Assert.Equal(0, provisioner.CallCount);
    }

    [Fact]
    public async Task ProvisioningWithoutCompatibleRuntimePreservesStructuredFailure()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService();
        var provisioner = new RecordingProvisioner();
        var resolver = CreateResolver(
            new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Auto },
            [],
            discovery,
            requiredMajorVersion: 25,
            provisioner);

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            resolver.ResolveAsync(CreateRequest("26.2", "forge-instance")));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound, exception.Reason);
        Assert.Equal(25, exception.RequiredMajorVersion);
        Assert.Equal(1, provisioner.CallCount);
    }

    [Fact]
    public async Task ProvisionedRuntimeOutsidePatchBoundaryIsRejected()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService();
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.16.5",
            LoaderKind.Forge,
            "36.2.25",
            metadataMajorVersion: 8);
        var provisioner = new RecordingProvisioner(_ =>
            discovery.Runtimes = [CreateRuntime(@"C:\Java\jdk-8u321\bin\java.exe", 8, "1.8.0_321")]);
        var resolver = CreateResolver(
            new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Auto },
            [],
            discovery,
            requirement,
            provisioner);

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            resolver.ResolveAsync(CreateRequest("1.16.5", "forge-instance")));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound, exception.Reason);
        Assert.Equal(1, provisioner.CallCount);
    }

    [Fact]
    public async Task ProvisioningFailureIsReportedAsStructuredJavaFailure()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService();
        var provisioner = new RecordingProvisioner(_ => throw new IOException("Download failed."));
        var resolver = CreateResolver(
            new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Auto },
            [],
            discovery,
            requiredMajorVersion: 25,
            provisioner);

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            resolver.ResolveAsync(CreateRequest("26.2", "forge-instance")));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, exception.Reason);
        Assert.Equal(25, exception.RequiredMajorVersion);
        Assert.IsType<IOException>(exception.InnerException);
    }

    private static LoaderInstallerJavaRuntimeResolver CreateResolver(
        LauncherSettings settings,
        IReadOnlyList<GameInstance> instances,
        RecordingJavaRuntimeDiscoveryService discovery,
        int? requiredMajorVersion,
        RecordingProvisioner provisioner)
    {
        return CreateResolver(
            settings,
            instances,
            discovery,
            new JavaRuntimeCompatibilityRequirement(
                requiredMajorVersion,
                requiredMajorVersion is int required
                    ? new JavaVersionBound(new JavaVersionNumber(required), true)
                    : null,
                null),
            provisioner);
    }

    private static LoaderInstallerJavaRuntimeResolver CreateResolver(
        LauncherSettings settings,
        IReadOnlyList<GameInstance> instances,
        RecordingJavaRuntimeDiscoveryService discovery,
        JavaRuntimeCompatibilityRequirement requirement,
        RecordingProvisioner provisioner)
    {
        return new LoaderInstallerJavaRuntimeResolver(
            _ => Task.FromResult(settings),
            (_, _) => Task.FromResult(instances),
            new JavaRuntimeSelectionService(discovery),
            discovery,
            new FixedRequirementResolver(requirement),
            provisioner);
    }

    private static LoaderInstallerJavaRuntimeRequest CreateRequest(
        string minecraftVersion,
        string versionName,
        IProgress<LauncherProgress>? progress = null)
    {
        return new LoaderInstallerJavaRuntimeRequest(
            minecraftVersion,
            versionName,
            LoaderKind.Forge,
            "62.0.0",
            @"C:\Games\.minecraft",
            DownloadSourcePreference.Official,
            8,
            progress);
    }

    private static JavaRuntimeInfo CreateRuntime(string path, int majorVersion, string? version = null)
    {
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            version ?? $"{majorVersion}.0.0",
            majorVersion,
            "x64",
            path,
            Path.GetDirectoryName(Path.GetDirectoryName(path))!,
            "Test");
    }

    private sealed class FixedRequirementResolver(JavaRuntimeCompatibilityRequirement requirement)
        : ILoaderInstallerJavaRequirementResolver
    {
        public Task<JavaRuntimeCompatibilityRequirement> ResolveRequirementAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(requirement);
    }

    private sealed class RecordingProvisioner(Action<LoaderInstallerJavaRuntimeRequest>? callback = null)
        : ILoaderInstallerJavaRuntimeProvisioner
    {
        public int CallCount { get; private set; }
        public LoaderInstallerJavaRuntimeRequest? LastRequest { get; private set; }

        public Task ProvisionAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            callback?.Invoke(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> Runtimes { get; set; } = [];
        public JavaRuntimeInfo? ManualRuntime { get; init; }
        public string? LastProbedPath { get; private set; }
        public string? LastMinecraftDirectory { get; private set; }

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            LastMinecraftDirectory = minecraftDirectory;
            return Task.FromResult(Runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            LastProbedPath = executablePath;
            if (ManualRuntime is null)
                throw new FileNotFoundException("Java runtime is unavailable.", executablePath);

            return Task.FromResult(ManualRuntime);
        }
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class VersionMetadataHandler(string minecraftVersion, int javaMajorVersion)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestUrl = request.RequestUri?.AbsoluteUri;
            var content = requestUrl switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => $$"""
                    {
                      "versions": [
                        {
                          "id": "{{minecraftVersion}}",
                          "url": "https://piston-meta.mojang.com/v1/packages/test/{{minecraftVersion}}.json"
                        }
                      ]
                    }
                    """,
                _ => $$"""
                    {
                      "id": "{{minecraftVersion}}",
                      "javaVersion": {
                        "component": "java-runtime-gamma",
                        "majorVersion": {{javaMajorVersion}}
                      }
                    }
                    """
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
