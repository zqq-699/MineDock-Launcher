/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Collections.Concurrent;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Accounts.ThirdParty;

internal sealed class ThirdPartyAccountService : IThirdPartyAccountService
{
    private const string ApiLocationHeader = "X-Authlib-Injector-API-Location";
    private readonly HttpClient httpClient;
    private readonly IThirdPartyAccountTokenStore tokenStore;
    private readonly IThirdPartyAccountAppearanceService? appearanceService;
    private readonly ILogger<ThirdPartyAccountService> logger;
    private readonly ConcurrentDictionary<string, PendingEmailLogin> pendingEmailLogins = new(StringComparer.Ordinal);
    private static readonly TimeSpan EmailLoginLifetime = TimeSpan.FromMinutes(10);

    public ThirdPartyAccountService(
        IThirdPartyAccountTokenStore tokenStore,
        LauncherPathProvider pathProvider,
        ILogger<ThirdPartyAccountService>? logger = null)
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        this.tokenStore = tokenStore;
        this.logger = logger ?? NullLogger<ThirdPartyAccountService>.Instance;
        var thirdPartyDataDirectory = Path.Combine(pathProvider.DefaultAccountDataDirectory, "third-party");
        appearanceService = new ThirdPartyAccountAppearanceService(
            httpClient,
            new AccountAvatarService(httpClient, Path.Combine(thirdPartyDataDirectory, "avatars")),
            new AccountSkinCacheService(httpClient, Path.Combine(thirdPartyDataDirectory, "skins")),
            new AccountCapeCacheService(httpClient, Path.Combine(thirdPartyDataDirectory, "capes")),
            this.logger);
    }

    internal ThirdPartyAccountService(
        HttpClient httpClient,
        IThirdPartyAccountTokenStore tokenStore,
        IThirdPartyAccountAppearanceService? appearanceService = null,
        ILogger<ThirdPartyAccountService>? logger = null)
    {
        this.httpClient = httpClient;
        this.tokenStore = tokenStore;
        this.appearanceService = appearanceService;
        this.logger = logger ?? NullLogger<ThirdPartyAccountService>.Instance;
    }

    public async Task<LauncherAccount> LoginWithUsernameAsync(
        string authenticationServer,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var initialUri = ParseServerUri(authenticationServer);
        try
        {
            var (apiRoot, metadataDocument) = await ResolveApiRootAsync(initialUri, cancellationToken).ConfigureAwait(false);
            using var metadata = metadataDocument;
            if (!SupportsUsernameLogin(metadata))
            {
                throw new ThirdPartyAccountLoginException(
                    ThirdPartyAccountLoginFailureReason.UsernameLoginUnsupported,
                    "The authentication server does not support username login.");
            }

            var clientToken = Guid.NewGuid().ToString("N");
            var authenticateUri = new Uri(EnsureTrailingSlash(apiRoot), "authserver/authenticate");
            using var request = new HttpRequestMessage(HttpMethod.Post, authenticateUri)
            {
                Content = JsonContent.Create(new
                {
                    username,
                    password,
                    clientToken,
                    requestUser = true,
                    agent = new { name = "Minecraft", version = 1 }
                })
            };
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new ThirdPartyAccountLoginException(
                    ThirdPartyAccountLoginFailureReason.InvalidCredentials,
                    "Invalid username or password.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new ThirdPartyAccountLoginException(
                    ThirdPartyAccountLoginFailureReason.InvalidResponse,
                    $"Authentication failed with HTTP status {(int)response.StatusCode}.");
            }

            using var payload = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var result = ParseAuthenticationResult(payload, username);
            var normalizedApiRoot = EnsureTrailingSlash(apiRoot).AbsoluteUri;
            var accountId = CreateAccountId(normalizedApiRoot, result.ProfileId);
            var profile = appearanceService is null
                ? ThirdPartyAccountProfileSnapshot.Unavailable
                : await appearanceService.GetProfileAsync(
                    apiRoot,
                    result.ProfileId,
                    accountId,
                    cancellationToken).ConfigureAwait(false);
            try
            {
                await tokenStore.SaveAsync(
                    accountId,
                    new ThirdPartyAccountTokens(result.AccessToken, result.ClientToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ThirdPartyAccountLoginException(
                    ThirdPartyAccountLoginFailureReason.CredentialStorageFailed,
                    "Third-party account credentials could not be saved.",
                    exception);
            }

            logger.LogInformation(
                "Third-party username login completed. AccountId={AccountId} AuthenticationServerHost={AuthenticationServerHost}",
                accountId,
                apiRoot.Host);
            var account = new LauncherAccount
            {
                Id = accountId,
                DisplayName = result.ProfileName,
                Kind = LauncherAccountKind.ThirdParty,
                Uuid = result.ProfileId,
                AuthenticationServerUrl = normalizedApiRoot,
                ThirdPartyLoginUsername = username.Trim()
            };
            return AccountMapper.WithThirdPartyProfile(
                account,
                profile.IsAvailable ? profile.ProfileName! : result.ProfileName,
                profile.AvatarSource,
                profile.Skin,
                profile.Cape);
        }
        catch (ThirdPartyAccountLoginException exception)
        {
            logger.LogWarning(
                exception,
                "Third-party username login failed. AuthenticationServerHost={AuthenticationServerHost} Reason={Reason}",
                initialUri.Host,
                exception.Reason);
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.ServerUnavailable,
                "The authentication server request timed out.",
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.ServerUnavailable,
                "The authentication server is unavailable.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidResponse,
                "The authentication server returned invalid JSON.",
                exception);
        }
    }

    public Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default) =>
        tokenStore.DeleteAsync(accountId, cancellationToken);

    public async Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(
        string authenticationServer,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        RemoveExpiredEmailLogins();
        var initialUri = ParseServerUri(authenticationServer);
        try
        {
            var (apiRoot, metadataDocument) = await ResolveApiRootAsync(initialUri, cancellationToken).ConfigureAwait(false);
            metadataDocument.Dispose();
            var authentication = await AuthenticateEmailAsync(apiRoot, email, password, cancellationToken).ConfigureAwait(false);
            if (authentication.Profiles.Count == 0)
                throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ProfileMissing, "The account has no profiles.");

            var normalizedApiRoot = EnsureTrailingSlash(apiRoot).AbsoluteUri;
            var snapshots = new ConcurrentDictionary<string, ThirdPartyAccountProfileSnapshot>(StringComparer.OrdinalIgnoreCase);
            using var concurrency = new SemaphoreSlim(4, 4);
            var options = await Task.WhenAll(authentication.Profiles.Select(async profile =>
            {
                await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var accountId = CreateAccountId(normalizedApiRoot, profile.Uuid);
                    var snapshot = ThirdPartyAccountProfileSnapshot.Unavailable;
                    if (appearanceService is not null)
                    {
                        try
                        {
                            snapshot = await appearanceService
                                .GetProfileAsync(apiRoot, profile.Uuid, accountId, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogDebug(
                                exception,
                                "Could not load a third-party profile avatar during email login. AuthenticationServerHost={AuthenticationServerHost}",
                                apiRoot.Host);
                        }
                    }
                    snapshots[profile.Uuid] = snapshot;
                    return new ThirdPartyProfileOption(
                        profile.Uuid,
                        snapshot.IsAvailable ? snapshot.ProfileName! : profile.Name,
                        snapshot.AvatarSource ?? LauncherAccount.DefaultSteveAvatarUrl);
                }
                finally
                {
                    concurrency.Release();
                }
            })).ConfigureAwait(false);

            var attemptId = Guid.NewGuid().ToString("N");
            pendingEmailLogins[attemptId] = new PendingEmailLogin(
                attemptId,
                apiRoot,
                normalizedApiRoot,
                email.Trim(),
                authentication,
                options,
                snapshots,
                DateTimeOffset.UtcNow.Add(EmailLoginLifetime));
            logger.LogInformation(
                "Third-party email login prepared profile selection. AuthenticationServerHost={AuthenticationServerHost} ProfileCount={ProfileCount}",
                apiRoot.Host,
                options.Length);
            return new ThirdPartyEmailLoginSession(attemptId, options);
        }
        catch (ThirdPartyAccountLoginException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ServerUnavailable, "The authentication server request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ServerUnavailable, "The authentication server is unavailable.", exception);
        }
        catch (JsonException exception)
        {
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, "The authentication server returned invalid JSON.", exception);
        }
    }

    public async Task<LauncherAccount> ImportEmailProfileAsync(
        string attemptId,
        string profileUuid,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!pendingEmailLogins.TryGetValue(attemptId, out var pending)
            || pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            pendingEmailLogins.TryRemove(attemptId, out _);
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, "The email login session has expired.");
        }
        var normalizedUuid = NormalizeUuid(profileUuid);
        var profile = pending.Profiles.FirstOrDefault(item =>
            string.Equals(NormalizeUuid(item.Uuid), normalizedUuid, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ProfileMissing, "The selected profile is unavailable.");

        await pending.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var authentication = pending.InitialConsumed
                ? await AuthenticateEmailAsync(pending.ApiRoot, pending.Email, password, cancellationToken).ConfigureAwait(false)
                : pending.InitialAuthentication;
            pending.InitialConsumed = true;
            if (authentication.Profiles.All(item => !string.Equals(
                    NormalizeUuid(item.Uuid), normalizedUuid, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ProfileMissing, "The selected profile no longer belongs to the account.");
            }

            var tokens = await BindEmailProfileAsync(
                pending.ApiRoot,
                authentication,
                profile,
                cancellationToken).ConfigureAwait(false);
            var accountId = CreateAccountId(pending.NormalizedApiRoot, profile.Uuid);
            try
            {
                await tokenStore.SaveAsync(accountId, tokens, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.CredentialStorageFailed, "Third-party account credentials could not be saved.", exception);
            }

            pending.Snapshots.TryGetValue(profile.Uuid, out var snapshot);
            var account = new LauncherAccount
            {
                Id = accountId,
                DisplayName = profile.Name,
                Kind = LauncherAccountKind.ThirdParty,
                Uuid = profile.Uuid,
                AuthenticationServerUrl = pending.NormalizedApiRoot,
                ThirdPartyLoginUsername = pending.Email
            };
            return snapshot is { IsAvailable: true }
                ? AccountMapper.WithThirdPartyProfile(account, snapshot.ProfileName!, snapshot.AvatarSource, snapshot.Skin, snapshot.Cape)
                : AccountMapper.WithThirdPartyProfile(account, profile.Name, null, null, null);
        }
        finally
        {
            pending.Gate.Release();
        }
    }

    public async Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        if (!pendingEmailLogins.TryRemove(attemptId, out var pending) || pending.InitialConsumed)
            return;
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                new Uri(EnsureTrailingSlash(pending.ApiRoot), "authserver/invalidate"),
                new
                {
                    accessToken = pending.InitialAuthentication.AccessToken,
                    clientToken = pending.InitialAuthentication.ClientToken
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Unable to invalidate an unused third-party email login token.");
        }
    }

    public async Task<LauncherAccount> ReauthenticateAsync(
        LauncherAccount account,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!account.IsThirdParty
            || string.IsNullOrWhiteSpace(account.AuthenticationServerUrl)
            || string.IsNullOrWhiteSpace(account.ThirdPartyLoginUsername)
            || string.IsNullOrWhiteSpace(account.Uuid))
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidResponse,
                "The existing third-party account identity is incomplete.");
        }

        LauncherAccount authenticated;
        if (account.ThirdPartyLoginUsername.Contains('@', StringComparison.Ordinal))
        {
            var selection = await BeginEmailLoginAsync(
                account.AuthenticationServerUrl,
                account.ThirdPartyLoginUsername,
                password,
                cancellationToken).ConfigureAwait(false);
            try
            {
                authenticated = await ImportEmailProfileAsync(
                    selection.AttemptId,
                    account.Uuid,
                    password,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CancelEmailLoginAsync(selection.AttemptId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        else
        {
            authenticated = await LoginWithUsernameAsync(
                account.AuthenticationServerUrl,
                account.ThirdPartyLoginUsername,
                password,
                cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(authenticated.Id, account.Id, StringComparison.Ordinal)
            || !string.Equals(
                NormalizeUuid(authenticated.Uuid),
                NormalizeUuid(account.Uuid),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await tokenStore.DeleteAsync(authenticated.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Unable to clean credentials returned for a mismatched third-party account. AccountId={AccountId}",
                    authenticated.Id);
            }
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.AccountMismatch,
                "The authenticated profile does not match the existing account.");
        }

        var activeSkin = authenticated.SkinLibrary.FirstOrDefault(skin =>
            string.Equals(skin.Id, authenticated.ActiveSkinId, StringComparison.Ordinal));
        var activeCape = authenticated.CachedCapeOptions.FirstOrDefault(cape => cape.IsActive && !cape.IsNone);
        return AccountMapper.WithThirdPartyProfile(
            account,
            authenticated.DisplayName,
            authenticated.AvatarSource,
            activeSkin,
            activeCape);
    }

    public async Task<LauncherAccount> RefreshAccountProfileAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        if (!account.IsThirdParty
            || appearanceService is null
            || string.IsNullOrWhiteSpace(account.AuthenticationServerUrl)
            || string.IsNullOrWhiteSpace(account.Uuid)
            || !Uri.TryCreate(account.AuthenticationServerUrl, UriKind.Absolute, out var apiRoot))
        {
            return account;
        }

        var profile = await appearanceService.GetProfileAsync(
            apiRoot,
            account.Uuid,
            account.Id,
            cancellationToken).ConfigureAwait(false);
        if (!profile.IsAvailable)
        {
            throw new ThirdPartyAccountProfileRefreshException(
                "The third-party account profile could not be refreshed.");
        }

        var refreshed = AccountMapper.WithThirdPartyProfile(
            account,
            profile.ProfileName!,
            profile.AvatarSource,
            profile.Skin,
            profile.Cape);
        logger.LogInformation(
            "Third-party account profile refreshed. AccountId={AccountId} HasSkin={HasSkin} HasCape={HasCape}",
            account.Id,
            profile.Skin is not null,
            profile.Cape is not null);
        return refreshed;
    }

    private async Task<(Uri ApiRoot, JsonDocument Metadata)> ResolveApiRootAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        using var initialResponse = await httpClient.GetAsync(initialUri, cancellationToken).ConfigureAwait(false);
        if (!initialResponse.IsSuccessStatusCode)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidResponse,
                $"Authentication server metadata failed with HTTP status {(int)initialResponse.StatusCode}.");
        }

        var responseUri = initialResponse.RequestMessage?.RequestUri ?? initialUri;
        var apiRoot = responseUri;
        if (initialResponse.Headers.TryGetValues(ApiLocationHeader, out var values))
        {
            var location = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(location))
                apiRoot = new Uri(responseUri, location);
        }
        EnsureHttps(apiRoot);

        if (UrisEqual(apiRoot, responseUri))
            return (apiRoot, await ReadJsonAsync(initialResponse, cancellationToken).ConfigureAwait(false));

        using var metadataResponse = await httpClient.GetAsync(apiRoot, cancellationToken).ConfigureAwait(false);
        if (!metadataResponse.IsSuccessStatusCode)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidResponse,
                $"Authentication server metadata failed with HTTP status {(int)metadataResponse.StatusCode}.");
        }
        return (apiRoot, await ReadJsonAsync(metadataResponse, cancellationToken).ConfigureAwait(false));
    }

    private async Task<EmailAuthenticationResult> AuthenticateEmailAsync(
        Uri apiRoot,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var clientToken = Guid.NewGuid().ToString("N");
        using var response = await httpClient.PostAsJsonAsync(
            new Uri(EnsureTrailingSlash(apiRoot), "authserver/authenticate"),
            new
            {
                username = email.Trim(),
                password,
                clientToken,
                requestUser = true,
                agent = new { name = "Minecraft", version = 1 }
            },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidCredentials, "Invalid email or password.");
        if (!response.IsSuccessStatusCode)
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, $"Authentication failed with HTTP status {(int)response.StatusCode}.");

        using var payload = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = payload.RootElement;
        var accessToken = GetRequiredString(root, "accessToken");
        var returnedClientToken = GetRequiredString(root, "clientToken");
        var profiles = new List<EmailProfile>();
        if (root.TryGetProperty("availableProfiles", out var availableProfiles))
        {
            if (availableProfiles.ValueKind != JsonValueKind.Array)
                throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, "availableProfiles is invalid.");
            foreach (var item in availableProfiles.EnumerateArray())
                AddEmailProfile(profiles, item);
        }

        EmailProfile? selectedProfile = null;
        if (root.TryGetProperty("selectedProfile", out var selectedElement)
            && selectedElement.ValueKind == JsonValueKind.Object)
        {
            selectedProfile = ParseEmailProfile(selectedElement);
            if (profiles.All(item => !string.Equals(item.Uuid, selectedProfile.Uuid, StringComparison.OrdinalIgnoreCase)))
                profiles.Add(selectedProfile);
        }
        return new EmailAuthenticationResult(accessToken, returnedClientToken, profiles, selectedProfile);
    }

    private async Task<ThirdPartyAccountTokens> BindEmailProfileAsync(
        Uri apiRoot,
        EmailAuthenticationResult authentication,
        ThirdPartyProfileOption profile,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            new Uri(EnsureTrailingSlash(apiRoot), "authserver/refresh"),
            new
            {
                accessToken = authentication.AccessToken,
                clientToken = authentication.ClientToken,
                requestUser = true,
                selectedProfile = new { id = NormalizeUuid(profile.Uuid), name = profile.Name }
            },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ProfileMissing, "The authentication server rejected the selected profile.");
        if (!response.IsSuccessStatusCode)
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, $"Profile selection failed with HTTP status {(int)response.StatusCode}.");
        using var payload = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = payload.RootElement;
        var accessToken = GetRequiredString(root, "accessToken");
        var clientToken = GetRequiredString(root, "clientToken");
        if (!string.Equals(clientToken, authentication.ClientToken, StringComparison.Ordinal)
            || !root.TryGetProperty("selectedProfile", out var selected)
            || selected.ValueKind != JsonValueKind.Object
            || !string.Equals(
                NormalizeUuid(ParseEmailProfile(selected).Uuid),
                NormalizeUuid(profile.Uuid),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, "The selected profile response does not match the requested profile.");
        }
        return new ThirdPartyAccountTokens(accessToken, clientToken);
    }

    private static void AddEmailProfile(List<EmailProfile> profiles, JsonElement element)
    {
        var profile = ParseEmailProfile(element);
        if (profiles.All(item => !string.Equals(item.Uuid, profile.Uuid, StringComparison.OrdinalIgnoreCase)))
            profiles.Add(profile);
    }

    private static EmailProfile ParseEmailProfile(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !Guid.TryParse(GetRequiredString(element, "id"), out var uuid))
            throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.InvalidResponse, "A profile UUID is invalid.");
        return new EmailProfile(uuid.ToString("D"), GetRequiredString(element, "name"));
    }

    private void RemoveExpiredEmailLogins()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in pendingEmailLogins)
        {
            if (pair.Value.ExpiresAt <= now)
                pendingEmailLogins.TryRemove(pair.Key, out _);
        }
    }

    private static Uri ParseServerUri(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = $"https://{trimmed}";
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidServerAddress,
                "The authentication server address is invalid.");
        }
        EnsureHttps(uri);
        return uri;
    }

    private static void EnsureHttps(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InsecureServerAddress,
                "Only HTTPS authentication servers are supported.");
        }
    }

    private static bool SupportsUsernameLogin(JsonDocument metadata)
    {
        return metadata.RootElement.TryGetProperty("meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("feature.non_email_login", out var feature)
            && feature.ValueKind is JsonValueKind.True;
    }

    private static AuthenticationResult ParseAuthenticationResult(JsonDocument payload, string username)
    {
        var root = payload.RootElement;
        var accessToken = GetRequiredString(root, "accessToken");
        var clientToken = GetRequiredString(root, "clientToken");
        if (!root.TryGetProperty("selectedProfile", out var profile) || profile.ValueKind != JsonValueKind.Object)
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.ProfileMissing,
                "The authentication server did not return a selected profile.");
        }

        var profileId = GetRequiredString(profile, "id");
        var profileName = GetRequiredString(profile, "name");
        if (!string.Equals(profileName, username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.ProfileMissing,
                "The selected profile does not match the requested username.");
        }
        if (!Guid.TryParse(profileId, out var parsedUuid))
        {
            throw new ThirdPartyAccountLoginException(
                ThirdPartyAccountLoginFailureReason.InvalidResponse,
                "The selected profile UUID is invalid.");
        }

        return new AuthenticationResult(
            accessToken,
            clientToken,
            parsedUuid.ToString("D"),
            profileName);
    }

    private static string GetRequiredString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }
        throw new ThirdPartyAccountLoginException(
            ThirdPartyAccountLoginFailureReason.InvalidResponse,
            $"Authentication response field '{name}' is missing.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri($"{uri.AbsoluteUri}/", UriKind.Absolute);

    private static bool UrisEqual(Uri left, Uri right) =>
        string.Equals(
            EnsureTrailingSlash(left).AbsoluteUri,
            EnsureTrailingSlash(right).AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);

    private static string CreateAccountId(string apiRoot, string profileId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{apiRoot}\n{profileId}"));
        return $"third-party-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string NormalizeUuid(string? value) =>
        Guid.TryParse(value, out var uuid) ? uuid.ToString("N") : string.Empty;

    private sealed record AuthenticationResult(
        string AccessToken,
        string ClientToken,
        string ProfileId,
        string ProfileName);

    private sealed record EmailProfile(string Uuid, string Name);

    private sealed record EmailAuthenticationResult(
        string AccessToken,
        string ClientToken,
        IReadOnlyList<EmailProfile> Profiles,
        EmailProfile? SelectedProfile);

    private sealed class PendingEmailLogin(
        string attemptId,
        Uri apiRoot,
        string normalizedApiRoot,
        string email,
        EmailAuthenticationResult initialAuthentication,
        IReadOnlyList<ThirdPartyProfileOption> profiles,
        ConcurrentDictionary<string, ThirdPartyAccountProfileSnapshot> snapshots,
        DateTimeOffset expiresAt)
    {
        public string AttemptId { get; } = attemptId;
        public Uri ApiRoot { get; } = apiRoot;
        public string NormalizedApiRoot { get; } = normalizedApiRoot;
        public string Email { get; } = email;
        public EmailAuthenticationResult InitialAuthentication { get; } = initialAuthentication;
        public IReadOnlyList<ThirdPartyProfileOption> Profiles { get; } = profiles;
        public ConcurrentDictionary<string, ThirdPartyAccountProfileSnapshot> Snapshots { get; } = snapshots;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool InitialConsumed { get; set; }
    }
}
