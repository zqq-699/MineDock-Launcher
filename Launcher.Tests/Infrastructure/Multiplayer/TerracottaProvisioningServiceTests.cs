/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Infrastructure.Multiplayer;

namespace Launcher.Tests.Infrastructure.Multiplayer;

public sealed class TerracottaProvisioningServiceTests
{
    [Fact]
    public async Task GiteePackageUsesGithubDigestAndPublishesOnlyRequiredFiles()
    {
        var archive = CreatePackage("0.4.3", "x86_64");
        using var context = CreateContext("0.4.3", archive, GithubDigest(archive));

        var module = await context.Service.EnsureAvailableAsync();

        Assert.Equal("0.4.3", module.Version);
        Assert.True(File.Exists(module.ExecutablePath));
        Assert.True(File.Exists(Path.Combine(module.DirectoryPath, "VCRUNTIME140.DLL")));
        Assert.Equal("gitee.com", context.Handler.AssetRequestHosts[0]);
        Assert.NotNull(context.Service.TryGetAvailable());
    }

    [Fact]
    public async Task UnexpectedArchiveEntryIsRejectedWithoutPublication()
    {
        var archive = CreatePackage("0.4.3", "x86_64", unexpectedFile: true);
        using var context = CreateContext(
            "0.4.3",
            archive,
            githubDigest: null,
            githubMetadataUnavailable: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Service.EnsureAvailableAsync());

        Assert.Null(context.Service.TryGetAvailable());
    }

    private static TestContext CreateContext(
        string version,
        byte[] githubArchive,
        string? githubDigest,
        byte[]? giteeArchive = null,
        bool githubMetadataUnavailable = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bhl-terracotta-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var handler = new TerracottaHttpMessageHandler(
            version,
            giteeArchive ?? githubArchive,
            githubArchive,
            githubDigest,
            githubMetadataUnavailable);
        var service = new TerracottaProvisioningService(
            new HttpClient(handler),
            root,
            "x86_64");
        return new TestContext(root, handler, service);
    }

    private static byte[] CreatePackage(
        string version,
        string architecture,
        bool includeRuntime = true,
        bool unexpectedFile = false)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            AddEntry(writer, $"terracotta-{version}-windows-{architecture}.exe", [0x4d, 0x5a, 1, 2, 3]);
            if (includeRuntime)
                AddEntry(writer, "VCRUNTIME140.DLL", [0x4d, 0x5a, 4, 5, 6]);
            if (unexpectedFile)
                AddEntry(writer, "unexpected.exe", [0x4d, 0x5a, 7]);
        }
        return output.ToArray();
    }

    private static void AddEntry(TarWriter writer, string name, byte[] content)
    {
        using var data = new MemoryStream(content);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = data
        };
        writer.WriteEntry(entry);
    }

    private static string GithubDigest(byte[] archive) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant()}";

    private sealed class TerracottaHttpMessageHandler(
        string version,
        byte[] giteeArchive,
        byte[] githubArchive,
        string? githubDigest,
        bool githubMetadataUnavailable) : HttpMessageHandler
    {
        public List<string> AssetRequestHosts { get; } = [];
        public bool AllMetadataUnavailable { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (AllMetadataUnavailable
                && ((uri.Host == "gitee.com" && uri.AbsolutePath.Contains("/api/", StringComparison.Ordinal))
                    || uri.Host == "api.github.com"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    RequestMessage = request
                });
            }
            if (uri.Host == "gitee.com" && uri.AbsolutePath.Contains("/api/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(request, GiteeMetadata(version)));
            if (uri.Host == "api.github.com")
            {
                return Task.FromResult(githubMetadataUnavailable
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request }
                    : JsonResponse(request, GithubMetadata(version, githubDigest)));
            }

            AssetRequestHosts.Add(uri.Host);
            var content = uri.Host == "gitee.com" ? giteeArchive : githubArchive;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            });
        }

        private static string GiteeMetadata(string version)
        {
            var asset = $"terracotta-{version}-windows-x86_64-pkg.tar.gz";
            return JsonSerializer.Serialize(new
            {
                tag_name = $"v{version}",
                prerelease = false,
                assets = new[]
                {
                    new
                    {
                        name = asset,
                        browser_download_url = $"https://gitee.com/burningtnt/Terracotta/releases/download/v{version}/{asset}"
                    }
                }
            });
        }

        private static string GithubMetadata(string version, string? digest)
        {
            var asset = $"terracotta-{version}-windows-x86_64-pkg.tar.gz";
            return JsonSerializer.Serialize(new
            {
                tag_name = $"v{version}",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new
                    {
                        name = asset,
                        browser_download_url = $"https://github.com/burningtnt/Terracotta/releases/download/v{version}/{asset}",
                        digest
                    }
                }
            });
        }

        private static HttpResponseMessage JsonResponse(HttpRequestMessage request, string json) => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(json)
        };
    }

    private sealed class TestContext(
        string moduleRoot,
        TerracottaHttpMessageHandler handler,
        TerracottaProvisioningService service) : IDisposable
    {
        public TerracottaHttpMessageHandler Handler { get; } = handler;
        public TerracottaProvisioningService Service { get; } = service;

        public void Dispose()
        {
            var fullPath = Path.GetFullPath(moduleRoot);
            if (fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
