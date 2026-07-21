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
