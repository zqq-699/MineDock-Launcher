using System.Text.Json.Serialization;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftProfileResponse
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

internal sealed class MinecraftProfileSkin
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed class MinecraftProfileCape
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed record ActiveCapeRequest([property: JsonPropertyName("capeId")] string CapeId);
