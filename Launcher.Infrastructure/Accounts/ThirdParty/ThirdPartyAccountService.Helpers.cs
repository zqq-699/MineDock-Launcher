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

    private static string? GetPlatformName(JsonDocument metadata)
    {
        if (!metadata.RootElement.TryGetProperty("meta", out var meta)
            || meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("serverName", out var serverName)
            || serverName.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = serverName.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
        string? platformName,
        string email,
        EmailAuthenticationResult initialAuthentication,
        IReadOnlyList<ThirdPartyProfileOption> profiles,
        ConcurrentDictionary<string, ThirdPartyAccountProfileSnapshot> snapshots,
        DateTimeOffset expiresAt)
    {
        public string AttemptId { get; } = attemptId;
        public Uri ApiRoot { get; } = apiRoot;
        public string NormalizedApiRoot { get; } = normalizedApiRoot;
        public string? PlatformName { get; } = platformName;
        public string Email { get; } = email;
        public EmailAuthenticationResult InitialAuthentication { get; } = initialAuthentication;
        public IReadOnlyList<ThirdPartyProfileOption> Profiles { get; } = profiles;
        public ConcurrentDictionary<string, ThirdPartyAccountProfileSnapshot> Snapshots { get; } = snapshots;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool InitialConsumed { get; set; }
    }
}
