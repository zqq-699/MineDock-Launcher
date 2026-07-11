/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Text;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Accounts.ThirdParty;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class ThirdPartyAccountServiceTests
{
    [Fact]
    public async Task EmailLoginReturnsAllProfilesWithoutUsernameFeature()
    {
        var handler = new StubHttpMessageHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "{\"meta\":{}}")
                : Json(HttpStatusCode.OK, """
                    {"accessToken":"access","clientToken":"client","availableProfiles":[
                      {"id":"00112233445566778899aabbccddeeff","name":"First"},
                      {"id":"11112222333344445555666677778888","name":"Second"}]}
                    """)));
        var service = new ThirdPartyAccountService(new HttpClient(handler), new RecordingTokenStore());

        var session = await service.BeginEmailLoginAsync("https://example.test/api/yggdrasil", "user@example.test", "password");

        Assert.Equal(["First", "Second"], session.Profiles.Select(profile => profile.Name));
        Assert.All(session.Profiles, profile => Assert.Equal(LauncherAccount.DefaultSteveAvatarUrl, profile.AvatarSource));
    }

    [Fact]
    public async Task EmailProfileImportRefreshesSelectedProfileAndStoresIndependentToken()
    {
        var refreshBodies = new List<string>();
        var authenticateCount = 0;
        var handler = new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, "{}");
            if (request.RequestUri!.AbsolutePath.EndsWith("/authenticate", StringComparison.Ordinal))
            {
                authenticateCount++;
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"accessToken\":\"access-{authenticateCount}\",\"clientToken\":\"client-{authenticateCount}\",\"availableProfiles\":[" +
                    "{\"id\":\"00112233445566778899aabbccddeeff\",\"name\":\"First\"}," +
                    "{\"id\":\"11112222333344445555666677778888\",\"name\":\"Second\"}]}");
            }
            var body = await request.Content!.ReadAsStringAsync();
            refreshBodies.Add(body);
            var isFirst = body.Contains("00112233445566778899aabbccddeeff", StringComparison.Ordinal);
            var suffix = isFirst ? "first" : "second";
            var profileId = isFirst ? "00112233445566778899aabbccddeeff" : "11112222333344445555666677778888";
            var profileName = isFirst ? "First" : "Second";
            return Json(
                HttpStatusCode.OK,
                $"{{\"accessToken\":\"bound-{suffix}\",\"clientToken\":\"client-{authenticateCount}\",\"selectedProfile\":{{\"id\":\"{profileId}\",\"name\":\"{profileName}\"}}}}");
        });
        var store = new RecordingTokenStore();
        var service = new ThirdPartyAccountService(new HttpClient(handler), store);
        var session = await service.BeginEmailLoginAsync("https://example.test", "user@example.test", "password");

        var first = await service.ImportEmailProfileAsync(session.AttemptId, session.Profiles[0].Uuid, "password");
        var second = await service.ImportEmailProfileAsync(session.AttemptId, session.Profiles[1].Uuid, "password");

        Assert.Equal("user@example.test", first.ThirdPartyLoginUsername);
        Assert.Equal("user@example.test", second.ThirdPartyLoginUsername);
        Assert.Equal(2, authenticateCount);
        Assert.Equal(2, refreshBodies.Count);
        Assert.Equal(2, store.SavedEntries.Count);
        Assert.NotEqual(first.Id, second.Id);
    }
    [Fact]
    public async Task LoginWithUsernameResolvesAliAndStoresReturnedTokens()
    {
        var requests = new List<(HttpMethod Method, Uri Uri, string Body)>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method, request.RequestUri!, body));
            if (request.RequestUri!.Host == "example.test")
            {
                var response = Json(HttpStatusCode.OK, "{}");
                response.Headers.Add("X-Authlib-Injector-API-Location", "https://auth.example.test/api/yggdrasil");
                return response;
            }
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, "{\"meta\":{\"feature.non_email_login\":true}}");
            return Json(HttpStatusCode.OK, """
                {"accessToken":"access-secret","clientToken":"client-secret","selectedProfile":{"id":"00112233445566778899aabbccddeeff","name":"Player"}}
                """);
        });
        var tokenStore = new RecordingTokenStore();
        var skin = new LauncherSkinRecord
        {
            Id = "skin-1",
            Source = "file:///skin.png",
            SkinModel = MinecraftSkinModel.Slim
        };
        var appearanceService = new RecordingAppearanceService(
            new ThirdPartyAccountProfileSnapshot(
                true,
                "00112233-4455-6677-8899-aabbccddeeff",
                "Player",
                "file:///avatar.png",
                skin.Source,
                skin.SkinModel,
                skin,
                null));
        var service = new ThirdPartyAccountService(new HttpClient(handler), tokenStore, appearanceService);

        var account = await service.LoginWithUsernameAsync("example.test", "player", "password");

        Assert.True(account.IsThirdParty);
        Assert.Equal("Player", account.DisplayName);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", account.Uuid);
        Assert.Equal("https://auth.example.test/api/yggdrasil/", account.AuthenticationServerUrl);
        Assert.Equal("player", account.ThirdPartyLoginUsername);
        Assert.Equal("file:///avatar.png", account.AvatarSource);
        Assert.Equal("file:///skin.png", account.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, account.SkinModel);
        var storedSkin = Assert.Single(account.SkinLibrary);
        Assert.Equal(skin.Id, storedSkin.Id);
        Assert.Equal(skin.Source, storedSkin.Source);
        Assert.Equal(skin.SkinModel, storedSkin.SkinModel);
        Assert.Equal("skin-1", account.ActiveSkinId);
        Assert.True(Assert.Single(account.CachedCapeOptions).IsNone);
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", appearanceService.ProfileId);
        Assert.Equal(new ThirdPartyAccountTokens("access-secret", "client-secret"), tokenStore.SavedTokens);
        Assert.Contains(requests, request =>
            request.Method == HttpMethod.Post
            && request.Uri.AbsoluteUri.EndsWith("/authserver/authenticate", StringComparison.Ordinal)
            && request.Body.Contains("\"username\":\"player\"", StringComparison.Ordinal)
            && request.Body.Contains("\"requestUser\":true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginWithUsernameMapsForbiddenToInvalidCredentials()
    {
        var handler = new StubHttpMessageHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "{\"meta\":{\"feature.non_email_login\":true}}")
                : Json(HttpStatusCode.Forbidden, "{\"error\":\"ForbiddenOperationException\"}")));
        var service = new ThirdPartyAccountService(new HttpClient(handler), new RecordingTokenStore());

        var exception = await Assert.ThrowsAsync<ThirdPartyAccountLoginException>(() =>
            service.LoginWithUsernameAsync("https://example.test/yggdrasil", "Player", "bad"));

        Assert.Equal(ThirdPartyAccountLoginFailureReason.InvalidCredentials, exception.Reason);
    }

    [Fact]
    public async Task LoginWithUsernameRequiresFeatureAndMatchingSelectedProfile()
    {
        var unsupported = new ThirdPartyAccountService(
            new HttpClient(new StubHttpMessageHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"meta\":{}}")))),
            new RecordingTokenStore());
        var unsupportedException = await Assert.ThrowsAsync<ThirdPartyAccountLoginException>(() =>
            unsupported.LoginWithUsernameAsync("https://example.test", "Player", "password"));
        Assert.Equal(ThirdPartyAccountLoginFailureReason.UsernameLoginUnsupported, unsupportedException.Reason);

        var mismatchHandler = new StubHttpMessageHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "{\"meta\":{\"feature.non_email_login\":true}}")
                : Json(HttpStatusCode.OK, """
                    {"accessToken":"a","clientToken":"c","selectedProfile":{"id":"00112233445566778899aabbccddeeff","name":"Other"}}
                    """)));
        var mismatch = new ThirdPartyAccountService(new HttpClient(mismatchHandler), new RecordingTokenStore());
        var mismatchException = await Assert.ThrowsAsync<ThirdPartyAccountLoginException>(() =>
            mismatch.LoginWithUsernameAsync("https://example.test", "Player", "password"));
        Assert.Equal(ThirdPartyAccountLoginFailureReason.ProfileMissing, mismatchException.Reason);
    }

    [Fact]
    public async Task LoginWithUsernameRejectsHttpBeforeSendingRequest()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP request should not be sent."));
        var service = new ThirdPartyAccountService(new HttpClient(handler), new RecordingTokenStore());

        var exception = await Assert.ThrowsAsync<ThirdPartyAccountLoginException>(() =>
            service.LoginWithUsernameAsync("http://example.test", "Player", "password"));

        Assert.Equal(ThirdPartyAccountLoginFailureReason.InsecureServerAddress, exception.Reason);
    }

    [Fact]
    public async Task ReauthenticationRejectsDifferentProfileAndRemovesReturnedCredentials()
    {
        var tokenStore = new RecordingTokenStore();
        var handler = new StubHttpMessageHandler(request => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "{\"meta\":{\"feature.non_email_login\":true}}")
                : Json(HttpStatusCode.OK, """
                    {"accessToken":"new-access","clientToken":"new-client","selectedProfile":{"id":"11112222333344445555666677778888","name":"Player"}}
                    """)));
        var service = new ThirdPartyAccountService(new HttpClient(handler), tokenStore);
        var existing = new LauncherAccount
        {
            Id = "third-party-existing",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            AuthenticationServerUrl = "https://example.test/api/yggdrasil/",
            ThirdPartyLoginUsername = "Player"
        };

        var exception = await Assert.ThrowsAsync<ThirdPartyAccountLoginException>(() =>
            service.ReauthenticateAsync(existing, "password"));

        Assert.Equal(ThirdPartyAccountLoginFailureReason.AccountMismatch, exception.Reason);
        Assert.Null(tokenStore.SavedTokens);
    }

    [Fact]
    public async Task RefreshProfileUpdatesNameAndAppearanceWhilePreservingIdentityAndSkinHistory()
    {
        var oldSkin = new LauncherSkinRecord
        {
            Id = "old-skin",
            Source = "file:///old-skin.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "old"
        };
        var newSkin = new LauncherSkinRecord
        {
            Id = "new-skin",
            Source = "file:///new-skin.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "new"
        };
        var appearanceService = new RecordingAppearanceService(
            new ThirdPartyAccountProfileSnapshot(
                true,
                "00112233-4455-6677-8899-aabbccddeeff",
                "RenamedPlayer",
                "file:///new-avatar.png",
                newSkin.Source,
                newSkin.SkinModel,
                newSkin,
                new AccountCapeOption
                {
                    Id = "third-party-current-cape",
                    DisplayName = string.Empty,
                    ImageUrl = "file:///cape.png",
                    IsActive = true
                }));
        var service = new ThirdPartyAccountService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            new RecordingTokenStore(),
            appearanceService);
        var account = new LauncherAccount
        {
            Id = "third-party-account",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            AuthenticationServerUrl = "https://auth.example.test/api/yggdrasil/",
            ThirdPartyLoginUsername = "Player",
            AvatarSource = "file:///old-avatar.png",
            SkinSource = oldSkin.Source,
            SkinModel = oldSkin.SkinModel,
            SkinLibrary = [oldSkin],
            ActiveSkinId = oldSkin.Id
        };

        var refreshed = await service.RefreshAccountProfileAsync(account);

        Assert.Equal(account.AuthenticationServerUrl, appearanceService.ApiRoot?.AbsoluteUri);
        Assert.Equal(account.Uuid, appearanceService.ProfileId);
        Assert.Equal(account.Id, appearanceService.AccountId);
        Assert.Equal("RenamedPlayer", refreshed.DisplayName);
        Assert.Equal(account.Id, refreshed.Id);
        Assert.Equal(account.Uuid, refreshed.Uuid);
        Assert.Equal(account.AuthenticationServerUrl, refreshed.AuthenticationServerUrl);
        Assert.Equal("Player", refreshed.ThirdPartyLoginUsername);
        Assert.Equal("file:///new-avatar.png", refreshed.AvatarSource);
        Assert.Equal("file:///new-skin.png", refreshed.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, refreshed.SkinModel);
        Assert.Equal("new-skin", refreshed.ActiveSkinId);
        Assert.Equal(["old-skin", "new-skin"], refreshed.SkinLibrary.Select(skin => skin.Id));
        Assert.Equal("file:///cape.png", Assert.Single(refreshed.CachedCapeOptions).ImageUrl);
    }

    [Fact]
    public async Task RefreshProfileFailurePreservesAllStoredProfileFields()
    {
        var appearanceService = new RecordingAppearanceService(ThirdPartyAccountProfileSnapshot.Unavailable);
        var service = new ThirdPartyAccountService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            new RecordingTokenStore(),
            appearanceService);
        var account = new LauncherAccount
        {
            Id = "third-party-account",
            DisplayName = "StoredPlayer",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            AuthenticationServerUrl = "https://auth.example.test/api/yggdrasil/",
            ThirdPartyLoginUsername = "login-name",
            AvatarSource = "file:///stored-avatar.png",
            SkinSource = "file:///stored-skin.png",
            CachedCapeOptions =
            [
                new AccountCapeOption
                {
                    Id = "stored-cape",
                    DisplayName = string.Empty,
                    ImageUrl = "file:///stored-cape.png",
                    IsActive = true
                }
            ]
        };

        await Assert.ThrowsAsync<ThirdPartyAccountProfileRefreshException>(() =>
            service.RefreshAccountProfileAsync(account));
        Assert.Equal("StoredPlayer", account.DisplayName);
        Assert.Equal("file:///stored-avatar.png", account.AvatarSource);
        Assert.Equal("file:///stored-skin.png", account.SkinSource);
        Assert.Equal("file:///stored-cape.png", Assert.Single(account.CachedCapeOptions).ImageUrl);
    }

    [Fact]
    public async Task RefreshProfileWithoutTexturesClearsCurrentSkinAndUsesNoCapeOption()
    {
        var oldSkin = new LauncherSkinRecord
        {
            Id = "old-skin",
            Source = "file:///old-skin.png",
            SkinModel = MinecraftSkinModel.Classic
        };
        var service = new ThirdPartyAccountService(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            new RecordingTokenStore(),
            new RecordingAppearanceService(new ThirdPartyAccountProfileSnapshot(
                true,
                "00112233-4455-6677-8899-aabbccddeeff",
                "PlayerWithoutTextures",
                null,
                null,
                null,
                null,
                null)));
        var account = new LauncherAccount
        {
            Id = "third-party-account",
            DisplayName = "OldPlayer",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            AuthenticationServerUrl = "https://auth.example.test/api/yggdrasil/",
            AvatarSource = "file:///old-avatar.png",
            SkinSource = oldSkin.Source,
            SkinModel = oldSkin.SkinModel,
            SkinLibrary = [oldSkin],
            ActiveSkinId = oldSkin.Id
        };

        var refreshed = await service.RefreshAccountProfileAsync(account);

        Assert.Equal("PlayerWithoutTextures", refreshed.DisplayName);
        Assert.Null(refreshed.AvatarSource);
        Assert.Null(refreshed.SkinSource);
        Assert.Null(refreshed.SkinModel);
        Assert.Null(refreshed.ActiveSkinId);
        Assert.Equal("old-skin", Assert.Single(refreshed.SkinLibrary).Id);
        var noCape = Assert.Single(refreshed.CachedCapeOptions);
        Assert.True(noCape.IsNone);
        Assert.True(noCape.IsActive);
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

    private sealed class RecordingTokenStore : IThirdPartyAccountTokenStore
    {
        public ThirdPartyAccountTokens? SavedTokens { get; private set; }
        public List<(string AccountId, ThirdPartyAccountTokens Tokens)> SavedEntries { get; } = [];

        public Task<ThirdPartyAccountTokens?> GetAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedTokens);

        public Task SaveAsync(
            string accountId,
            ThirdPartyAccountTokens tokens,
            CancellationToken cancellationToken = default)
        {
            SavedTokens = tokens;
            SavedEntries.Add((accountId, tokens));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
        {
            SavedTokens = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAppearanceService(ThirdPartyAccountProfileSnapshot profile)
        : IThirdPartyAccountAppearanceService
    {
        public Uri? ApiRoot { get; private set; }
        public string? ProfileId { get; private set; }
        public string? AccountId { get; private set; }

        public Task<ThirdPartyAccountProfileSnapshot> GetProfileAsync(
            Uri apiRoot,
            string profileId,
            string accountId,
            CancellationToken cancellationToken)
        {
            ApiRoot = apiRoot;
            ProfileId = profileId;
            AccountId = accountId;
            return Task.FromResult(profile);
        }
    }
}
