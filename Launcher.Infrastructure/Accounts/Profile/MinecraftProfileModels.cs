/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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

    [JsonPropertyName("variant")]
    public string? Variant { get; set; }
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
