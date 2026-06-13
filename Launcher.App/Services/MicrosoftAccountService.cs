using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.App.Models;
using Launcher.Core.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher.App.Services;

public sealed class MicrosoftAccountService : IMicrosoftAccountService
{
    private const int AvatarSize = 64;
    private const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly JELoginHandler loginHandler;
    private readonly string avatarDirectory;

    public MicrosoftAccountService()
    {
        var accountDirectory = Path.Combine(LauncherDefaults.DefaultDataDirectory, "accounts", "microsoft");
        var accountFile = Path.Combine(accountDirectory, "accounts.json");
        avatarDirectory = Path.Combine(accountDirectory, "avatars");
        Directory.CreateDirectory(accountDirectory);
        Directory.CreateDirectory(avatarDirectory);

        loginHandler = new JELoginHandlerBuilder()
            .WithAccountManager(accountFile)
            .Build();
    }

    public async Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = new List<LauncherAccount>();
        foreach (var account in loginHandler.AccountManager.GetAccounts().OfType<JEGameAccount>())
        {
            var profile = account.Profile;
            if (profile is null
                || string.IsNullOrWhiteSpace(profile.Username)
                || string.IsNullOrWhiteSpace(profile.UUID))
            {
                continue;
            }

            accounts.Add(await CreateAccountFromProfileAsync(profile, forceRefreshAvatar: false, cancellationToken));
        }

        return accounts;
    }

    public async Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        var account = loginHandler.AccountManager.NewAccount();
        var session = await loginHandler.AuthenticateInteractively(account, cancellationToken);
        loginHandler.AccountManager.SaveAccounts();

        var profile = JEGameAccount.FromSessionStorage(account.SessionStorage).Profile;
        if (profile is not null
            && !string.IsNullOrWhiteSpace(profile.Username)
            && !string.IsNullOrWhiteSpace(profile.UUID))
        {
            return await CreateAccountFromProfileAsync(profile, forceRefreshAvatar: true, cancellationToken);
        }

        var uuid = NormalizeUuid(session.UUID);
        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = session.Username ?? string.Empty,
            Uuid = uuid,
            IsOffline = false
        };
    }

    public async Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid))
            return;

        foreach (var savedAccount in loginHandler.AccountManager.GetAccounts().OfType<JEGameAccount>())
        {
            var savedUuid = NormalizeUuid(savedAccount.Profile?.UUID);
            if (!string.Equals(savedUuid, account.Uuid, StringComparison.OrdinalIgnoreCase))
                continue;

            await loginHandler.Signout(savedAccount, cancellationToken);
            loginHandler.AccountManager.SaveAccounts();
            DeleteAvatar(account.Uuid);
            return;
        }
    }

    public async Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetMinecraftProfileAsync(account, cancellationToken);
        var capes = profile.Capes ?? [];
        if (capes.Count == 0)
            return [];

        var options = new List<AccountCapeOption>
        {
            new()
            {
                Id = null,
                DisplayName = "\u4e0d\u4f7f\u7528\u62ab\u98ce",
                IsActive = capes.All(cape => !IsActiveState(cape.State)),
                IsNone = true
            }
        };

        options.AddRange(capes.Select(cape => new AccountCapeOption
        {
            Id = cape.Id,
            DisplayName = string.IsNullOrWhiteSpace(cape.Alias) ? cape.Id ?? "\u672a\u547d\u540d\u62ab\u98ce" : cape.Alias,
            IsActive = IsActiveState(cape.State),
            IsNone = false
        }));

        return options;
    }

    public async Task<LauncherAccount> UploadSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(skinFilePath))
            throw new FileNotFoundException("\u627e\u4e0d\u5230\u8981\u4e0a\u4f20\u7684\u76ae\u80a4\u6587\u4ef6", skinFilePath);

        var accessToken = await GetAccessTokenAsync(account, cancellationToken);
        await using var fileStream = File.OpenRead(skinFilePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("classic", Encoding.UTF8), "variant");

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(skinFilePath));

        using var request = CreateProfileRequest(HttpMethod.Put, $"{MinecraftProfileEndpoint}/skins", accessToken);
        request.Content = content;
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "\u4e0a\u4f20\u76ae\u80a4\u5931\u8d25", cancellationToken);

        var profile = await GetMinecraftProfileAsync(account, cancellationToken);
        return await CreateAccountFromProfileAsync(profile, forceRefreshAvatar: true, cancellationToken);
    }

    public async Task SetActiveCapeAsync(
        LauncherAccount account,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(account, cancellationToken);
        if (string.IsNullOrWhiteSpace(capeId))
        {
            using var deleteRequest = CreateProfileRequest(HttpMethod.Delete, $"{MinecraftProfileEndpoint}/capes/active", accessToken);
            using var deleteResponse = await HttpClient.SendAsync(deleteRequest, cancellationToken);
            await EnsureSuccessAsync(deleteResponse, "\u79fb\u9664\u62ab\u98ce\u5931\u8d25", cancellationToken);
            return;
        }

        using var request = CreateProfileRequest(HttpMethod.Put, $"{MinecraftProfileEndpoint}/capes/active", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new ActiveCapeRequest(capeId), JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "\u66f4\u6362\u62ab\u98ce\u5931\u8d25", cancellationToken);
    }

    private async Task<LauncherAccount> CreateAccountFromProfileAsync(
        JEProfile profile,
        bool forceRefreshAvatar,
        CancellationToken cancellationToken)
    {
        var uuid = NormalizeUuid(profile.UUID);
        var avatarSource = await GetOrCreateAvatarSourceAsync(
            uuid,
            GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Username ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            IsOffline = false
        };
    }

    private async Task<LauncherAccount> CreateAccountFromProfileAsync(
        MinecraftProfileResponse profile,
        bool forceRefreshAvatar,
        CancellationToken cancellationToken)
    {
        var uuid = NormalizeUuid(profile.Id);
        var avatarSource = await GetOrCreateAvatarSourceAsync(
            uuid,
            GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Name ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            IsOffline = false
        };
    }

    private async Task<string?> GetOrCreateAvatarSourceAsync(
        string uuid,
        string? skinUrl,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var avatarPath = Path.Combine(avatarDirectory, $"{uuid}.png");
        if (!forceRefresh && File.Exists(avatarPath))
            return new Uri(avatarPath).AbsoluteUri;

        if (string.IsNullOrWhiteSpace(skinUrl))
            return null;

        try
        {
            var skinBytes = await HttpClient.GetByteArrayAsync(skinUrl, cancellationToken);
            var skin = LoadBitmap(skinBytes);
            var avatar = CreateAvatarBitmap(skin);
            SavePng(avatar, avatarPath);
            return new Uri(avatarPath).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetActiveSkinUrl(JEProfile profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => string.Equals(skin.State, "ACTIVE", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    private static string? GetActiveSkinUrl(MinecraftProfileResponse profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State) && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    private async Task<MinecraftProfileResponse> GetMinecraftProfileAsync(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(account, cancellationToken);
        using var request = CreateProfileRequest(HttpMethod.Get, MinecraftProfileEndpoint, accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "\u83b7\u53d6\u6b63\u7248\u8d26\u6237\u8d44\u6599\u5931\u8d25", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var profile = await JsonSerializer.DeserializeAsync<MinecraftProfileResponse>(stream, JsonOptions, cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException("\u672a\u80fd\u8bfb\u53d6\u5230 Minecraft Java \u8d26\u6237\u8d44\u6599");

        return profile;
    }

    private async Task<string> GetAccessTokenAsync(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (account.IsOffline || string.IsNullOrWhiteSpace(account.Uuid))
            throw new InvalidOperationException("\u53ea\u6709\u6b63\u7248\u8d26\u6237\u652f\u6301\u6b64\u64cd\u4f5c");

        var savedAccount = FindSavedAccount(account)
            ?? throw new InvalidOperationException("\u672a\u627e\u5230\u8fd9\u4e2a\u6b63\u7248\u8d26\u6237\u7684\u767b\u5f55\u7f13\u5b58\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55");

        await loginHandler.AuthenticateSilently(savedAccount, cancellationToken);
        loginHandler.AccountManager.SaveAccounts();

        var refreshedAccount = JEGameAccount.FromSessionStorage(savedAccount.SessionStorage);
        var accessToken = refreshedAccount.Token?.AccessToken ?? savedAccount.Token?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("\u672a\u80fd\u83b7\u53d6\u6b63\u7248\u8bbf\u95ee\u4ee4\u724c\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55");

        return accessToken;
    }

    private JEGameAccount? FindSavedAccount(LauncherAccount account)
    {
        var targetUuid = NormalizeUuid(account.Uuid);
        return loginHandler.AccountManager.GetAccounts()
            .OfType<JEGameAccount>()
            .FirstOrDefault(savedAccount =>
            {
                var savedUuid = NormalizeUuid(savedAccount.Profile?.UUID);
                if (string.IsNullOrWhiteSpace(savedUuid))
                    savedUuid = NormalizeUuid(JEGameAccount.FromSessionStorage(savedAccount.SessionStorage).Profile?.UUID);

                return string.Equals(savedUuid, targetUuid, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static HttpRequestMessage CreateProfileRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
            ? $"{message}\uff1aHTTP {(int)response.StatusCode}"
            : $"{message}\uff1a{details}");
    }

    private static bool IsActiveState(string? state)
    {
        return string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUuid(string? uuid)
    {
        return uuid?.Replace("-", string.Empty, StringComparison.Ordinal) ?? string.Empty;
    }

    private static BitmapSource LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateAvatarBitmap(BitmapSource skin)
    {
        var source = EnsureBgra32(skin);
        var face = ReadPixels(source, 8, 8, 8, 8);
        var overlay = source.PixelWidth >= 48 && source.PixelHeight >= 16
            ? ReadPixels(source, 40, 8, 8, 8)
            : null;

        var output = new byte[AvatarSize * AvatarSize * 4];
        for (var y = 0; y < AvatarSize; y++)
        {
            var sourceY = y * 8 / AvatarSize;
            for (var x = 0; x < AvatarSize; x++)
            {
                var sourceX = x * 8 / AvatarSize;
                var sourceIndex = (sourceY * 8 + sourceX) * 4;
                var outputIndex = (y * AvatarSize + x) * 4;

                output[outputIndex] = face[sourceIndex];
                output[outputIndex + 1] = face[sourceIndex + 1];
                output[outputIndex + 2] = face[sourceIndex + 2];
                output[outputIndex + 3] = face[sourceIndex + 3];

                if (overlay is not null)
                    BlendPixel(output, outputIndex, overlay, sourceIndex);
            }
        }

        var avatar = BitmapSource.Create(
            AvatarSize,
            AvatarSize,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            AvatarSize * 4);
        avatar.Freeze();
        return avatar;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] ReadPixels(BitmapSource source, int x, int y, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        source.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        return pixels;
    }

    private static void BlendPixel(byte[] target, int targetIndex, byte[] overlay, int overlayIndex)
    {
        var overlayAlpha = overlay[overlayIndex + 3];
        if (overlayAlpha == 0)
            return;

        if (overlayAlpha == byte.MaxValue)
        {
            target[targetIndex] = overlay[overlayIndex];
            target[targetIndex + 1] = overlay[overlayIndex + 1];
            target[targetIndex + 2] = overlay[overlayIndex + 2];
            target[targetIndex + 3] = byte.MaxValue;
            return;
        }

        var inverseAlpha = byte.MaxValue - overlayAlpha;
        target[targetIndex] = (byte)((overlay[overlayIndex] * overlayAlpha + target[targetIndex] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 1] = (byte)((overlay[overlayIndex + 1] * overlayAlpha + target[targetIndex + 1] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 2] = (byte)((overlay[overlayIndex + 2] * overlayAlpha + target[targetIndex + 2] * inverseAlpha) / byte.MaxValue);
        target[targetIndex + 3] = byte.MaxValue;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void DeleteAvatar(string uuid)
    {
        var avatarPath = Path.Combine(avatarDirectory, $"{uuid}.png");
        if (File.Exists(avatarPath))
            File.Delete(avatarPath);
    }

    private sealed class MinecraftProfileResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("skins")]
        public List<MinecraftProfileSkin>? Skins { get; set; }

        [JsonPropertyName("capes")]
        public List<MinecraftProfileCape>? Capes { get; set; }
    }

    private sealed class MinecraftProfileSkin
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class MinecraftProfileCape
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("alias")]
        public string? Alias { get; set; }
    }

    private sealed record ActiveCapeRequest([property: JsonPropertyName("capeId")] string CapeId);
}
