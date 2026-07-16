/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts;
using Launcher.Infrastructure.Accounts.ThirdParty;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class ThirdPartyAccountAppearanceServiceTests : IDisposable
{
    private const string ProfileId = "00112233-4455-6677-8899-aabbccddeeff";
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "launcher-third-party-appearance-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetProfileDownloadsCurrentSkinCapeAndAvatar()
    {
        const string skinUrl = "http://127.0.0.1/skin.png";
        const string capeUrl = "http://127.0.0.1/cape.png";
        var texturesPayload = CreateTexturesPayload(skinUrl, capeUrl, "slim");
        var requestedUris = new List<Uri>();
        var textureBytes = CreateTexturePng();
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "auth.example.test")
            {
                return Task.FromResult(ProfileJson(ProfileId, "RenamedPlayer", texturesPayload));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(textureBytes)
            });
        });
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var profile = await service.GetProfileAsync(
            new Uri("https://auth.example.test/api/yggdrasil/"),
            ProfileId,
            "third-party-account",
            CancellationToken.None);

        Assert.True(profile.IsAvailable);
        Assert.Equal(ProfileId, profile.ProfileId);
        Assert.Equal("RenamedPlayer", profile.ProfileName);
        Assert.NotNull(profile.Skin);
        Assert.Equal(MinecraftSkinModel.Slim, profile.SkinModel);
        Assert.True(new Uri(profile.SkinSource!).IsFile);
        Assert.True(File.Exists(new Uri(profile.SkinSource!).LocalPath));
        Assert.True(new Uri(profile.AvatarSource!).IsFile);
        Assert.True(File.Exists(new Uri(profile.AvatarSource!).LocalPath));
        Assert.True(new Uri(profile.Cape!.ImageUrl!).IsFile);
        Assert.True(File.Exists(new Uri(profile.Cape.ImageUrl!).LocalPath));
        Assert.True(profile.Cape.IsActive);
        Assert.Contains(requestedUris, uri => uri.AbsoluteUri ==
            "https://auth.example.test/api/yggdrasil/sessionserver/session/minecraft/profile/00112233445566778899aabbccddeeff");
        Assert.Equal(2, requestedUris.Count(uri => uri.AbsoluteUri == skinUrl));
        Assert.Single(requestedUris, uri => uri.AbsoluteUri == capeUrl);
    }

    [Fact]
    public async Task GetProfileTreatsMissingTexturesAsAuthoritativeEmptyAppearance()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(
            ProfileJson(ProfileId, "Player", texturesPayload: null))));
        var service = CreateService(httpClient);

        var profile = await service.GetProfileAsync(
            new Uri("https://auth.example.test/api/yggdrasil/"),
            ProfileId,
            "third-party-account",
            CancellationToken.None);

        Assert.True(profile.IsAvailable);
        Assert.Equal("Player", profile.ProfileName);
        Assert.Null(profile.Skin);
        Assert.Null(profile.Cape);
        Assert.Null(profile.AvatarSource);
    }

    [Theory]
    [InlineData("ffeeddcc-bbaa-9988-7766-554433221100", "Player")]
    [InlineData("00112233-4455-6677-8899-aabbccddeeff", "")]
    public async Task GetProfileRejectsMismatchedUuidOrMissingName(string returnedId, string returnedName)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(
            ProfileJson(returnedId, returnedName, texturesPayload: null))));
        var service = CreateService(httpClient);

        var profile = await service.GetProfileAsync(
            new Uri("https://auth.example.test/api/yggdrasil/"),
            ProfileId,
            "third-party-account",
            CancellationToken.None);

        Assert.False(profile.IsAvailable);
    }

    [Fact]
    public void ParseTexturesRejectsNonHttpTextureUrl()
    {
        var texturesPayload = CreateTexturesPayload("ftp://textures.example.test/skin.png", null, null);
        using var profile = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            properties = new[] { new { name = "textures", value = texturesPayload } }
        }));

        Assert.Throws<JsonException>(() => ThirdPartyAccountAppearanceService.ParseTextures(profile.RootElement));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }

    private ThirdPartyAccountAppearanceService CreateService(HttpClient httpClient) => new(
        httpClient,
        new AccountAvatarService(httpClient, Path.Combine(tempDirectory, "avatars")),
        new AccountSkinCacheService(httpClient, Path.Combine(tempDirectory, "skins")),
        new AccountCapeCacheService(httpClient, Path.Combine(tempDirectory, "capes")));

    private static string CreateTexturesPayload(string? skinUrl, string? capeUrl, string? model)
    {
        var textures = new Dictionary<string, object>();
        if (skinUrl is not null)
        {
            textures["SKIN"] = model is null
                ? new { url = skinUrl }
                : new { url = skinUrl, metadata = new { model } };
        }
        if (capeUrl is not null)
            textures["CAPE"] = new { url = capeUrl };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { textures })));
    }

    private static HttpResponseMessage ProfileJson(string id, string name, string? texturesPayload)
    {
        var properties = texturesPayload is null
            ? Array.Empty<object>()
            : [new { name = "textures", value = texturesPayload }];
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, name, properties }));
    }

    private static byte[] CreateTexturePng()
    {
        var pixels = new byte[64 * 64 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 80;
            pixels[index + 1] = 140;
            pixels[index + 2] = 220;
            pixels[index + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            64,
            64,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            64 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
