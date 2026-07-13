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

internal sealed partial class ThirdPartyAccountService
{
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
}
