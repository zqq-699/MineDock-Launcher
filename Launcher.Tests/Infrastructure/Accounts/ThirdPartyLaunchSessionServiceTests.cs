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

public sealed class ThirdPartyLaunchSessionServiceTests
{
    [Fact]
    public async Task ValidTokenCreatesSessionWithoutRefreshing()
    {
        var store = new RecordingTokenStore(new ThirdPartyAccountTokens("access", "client"));
        var requests = new List<Uri>();
        var service = new ThirdPartyLaunchSessionService(
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                requests.Add(request.RequestUri!);
                return Task.FromResult(request.Method == HttpMethod.Get
                    ? Json(HttpStatusCode.OK, "{\"skinDomains\":[\"example.test\"]}")
                    : new HttpResponseMessage(HttpStatusCode.NoContent));
            })),
            store);

        var session = await service.CreateAsync(CreateAccount());

        Assert.Equal("Player", session.Username);
        Assert.Equal("access", session.AccessToken);
        Assert.Equal("00112233445566778899aabbccddeeff", session.Uuid);
        Assert.Equal("https://example.test/api/yggdrasil/", session.AuthenticationServerUrl);
        Assert.Equal("{\"skinDomains\":[\"example.test\"]}",
            Encoding.UTF8.GetString(Convert.FromBase64String(session.PrefetchedMetadata)));
        Assert.Equal(2, requests.Count);
        Assert.Null(store.SavedTokens);
    }

    [Fact]
    public async Task RefreshRejectsDifferentProfileUuidWithoutOverwritingTokens()
    {
        var store = new RecordingTokenStore(new ThirdPartyAccountTokens("old-access", "client"));
        var service = new ThirdPartyLaunchSessionService(
            new HttpClient(new StubHttpMessageHandler(request => Task.FromResult(
                request.Method == HttpMethod.Get
                    ? Json(HttpStatusCode.OK, "{}")
                    : request.RequestUri!.AbsolutePath.EndsWith("/validate", StringComparison.Ordinal)
                        ? Json(HttpStatusCode.Forbidden, "{}")
                        : Json(HttpStatusCode.OK, """
                            {"accessToken":"new-access","clientToken":"client","selectedProfile":{"id":"11112222333344445555666677778888","name":"Other"}}
                            """)))),
            store);

        var exception = await Assert.ThrowsAsync<LaunchAccountSessionException>(() =>
            service.CreateAsync(CreateAccount()));

        Assert.Equal(LaunchAccountSessionFailureReason.InvalidAuthenticationResponse, exception.Reason);
        Assert.Null(store.SavedTokens);
    }

    private static LauncherAccount CreateAccount() => new()
    {
        Id = "third-party",
        DisplayName = "Player",
        Kind = LauncherAccountKind.ThirdParty,
        Uuid = "00112233-4455-6677-8899-aabbccddeeff",
        AuthenticationServerUrl = "https://example.test/api/yggdrasil/",
        ThirdPartyLoginUsername = "Player"
    };

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

    private sealed class RecordingTokenStore(ThirdPartyAccountTokens? tokens) : IThirdPartyAccountTokenStore
    {
        public ThirdPartyAccountTokens? SavedTokens { get; private set; }

        public Task<ThirdPartyAccountTokens?> GetAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tokens);

        public Task SaveAsync(
            string accountId,
            ThirdPartyAccountTokens savedTokens,
            CancellationToken cancellationToken = default)
        {
            SavedTokens = savedTokens;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
