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

using System.Text;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 按 Minecraft 版本继承语义合并父子 JSON，并规范化新旧两代启动参数格式。
/// </summary>
internal static class VersionJsonMergeHelper
{
    // 这些命令行选项只能保留一个最终值，规范化时采用最后出现的声明。
    private static readonly HashSet<string> SingleValueMinecraftArgumentOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--username",
        "--version",
        "--gameDir",
        "--assetsDir",
        "--assetIndex",
        "--uuid",
        "--accessToken",
        "--userType",
        "--userProperties",
        "--versionType",
        "--width",
        "--height"
    };

    public static JsonObject MergeFlattenedVersion(
        JsonObject baseVersion,
        JsonObject derivedVersion,
        string versionName,
        string? minecraftVersion = null)
    {
        // 从父版本深拷贝开始，普通属性由派生版本覆盖，集合按各自协议规则处理。
        var mergedVersion = (JsonObject)baseVersion.DeepClone();

        foreach (var property in derivedVersion)
        {
            switch (property.Key)
            {
                case "id":
                case "inheritsFrom":
                case "jar":
                    continue;
                case "libraries":
                    mergedVersion["libraries"] = MergeLibraries(
                        mergedVersion["libraries"] as JsonArray,
                        property.Value as JsonArray);
                    break;
                case "arguments":
                    mergedVersion["arguments"] = MergeArguments(
                        mergedVersion["arguments"] as JsonObject,
                        property.Value as JsonObject);
                    break;
                case "minecraftArguments":
                    mergedVersion["minecraftArguments"] = MergeMinecraftArguments(
                        mergedVersion["minecraftArguments"]?.GetValue<string>(),
                        property.Value?.GetValue<string>());
                    break;
                default:
                    mergedVersion[property.Key] = property.Value?.DeepClone();
                    break;
            }
        }

        mergedVersion["id"] = versionName;
        mergedVersion["jar"] = versionName;
        mergedVersion.Remove("inheritsFrom");
        LauncherVersionMetadata.Apply(mergedVersion, minecraftVersion ?? string.Empty);
        return mergedVersion;
    }

    public static JsonArray MergeLibraries(JsonArray? baseLibraries, JsonArray? derivedLibraries)
    {
        // 以 Maven name 作为身份并保留首次声明，避免继承链重复下载同一坐标。
        var mergedLibraries = new JsonArray();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendLibraries(baseLibraries);
        AppendLibraries(derivedLibraries);
        return mergedLibraries;

        void AppendLibraries(JsonArray? source)
        {
            if (source is null)
                return;

            foreach (var library in source)
            {
                if (library is null)
                    continue;

                var key = library is JsonObject libraryObject
                    && libraryObject["name"] is JsonValue libraryNameValue
                    && libraryNameValue.TryGetValue<string>(out var libraryName)
                    && !string.IsNullOrWhiteSpace(libraryName)
                        ? libraryName
                        : library.ToJsonString();

                if (!seenNames.Add(key))
                    continue;

                mergedLibraries.Add(library.DeepClone());
            }
        }
    }

    public static JsonObject MergeArguments(JsonObject? baseArguments, JsonObject? derivedArguments)
    {
        // game/jvm 数组按父后子拼接，带 rules 的对象保持原结构而不是转成字符串。
        var mergedArguments = baseArguments is null
            ? new JsonObject()
            : (JsonObject)baseArguments.DeepClone();

        if (derivedArguments is null)
            return mergedArguments;

        foreach (var property in derivedArguments)
        {
            if (mergedArguments[property.Key] is JsonArray baseArray
                && property.Value is JsonArray derivedArray)
            {
                var mergedArray = new JsonArray();
                foreach (var item in baseArray)
                    mergedArray.Add(item?.DeepClone());

                foreach (var item in derivedArray)
                    mergedArray.Add(item?.DeepClone());

                mergedArguments[property.Key] = string.Equals(property.Key, "game", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeGameArgumentArray(mergedArray)
                    : mergedArray;
                continue;
            }

            mergedArguments[property.Key] = property.Value?.DeepClone();
        }

        if (mergedArguments["game"] is JsonArray gameArguments)
            mergedArguments["game"] = NormalizeGameArgumentArray(gameArguments);

        return mergedArguments;
    }

    public static string MergeMinecraftArguments(string? baseArguments, string? derivedArguments)
    {
        // 旧格式先拼接父子命令行，再由 Normalize 移除被派生值覆盖的单值选项。
        if (string.IsNullOrWhiteSpace(baseArguments))
            return NormalizeMinecraftArguments(derivedArguments);

        if (string.IsNullOrWhiteSpace(derivedArguments))
            return NormalizeMinecraftArguments(baseArguments);

        return NormalizeMinecraftArguments($"{baseArguments} {derivedArguments}");
    }

    public static string NormalizeMinecraftArguments(string? arguments)
    {
        // 先做保引号切词，再从右向左意义上保留单值选项最后一次出现的位置。
        if (string.IsNullOrWhiteSpace(arguments))
            return string.Empty;

        var tokens = SplitCommandLine(arguments);
        if (tokens.Count == 0)
            return string.Empty;

        var keep = Enumerable.Repeat(true, tokens.Count).ToArray();
        var lastOptionIndexes = new Dictionary<string, (int OptionIndex, int? ValueIndex)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var optionName = GetOptionName(token);
            if (optionName is null || !SingleValueMinecraftArgumentOptions.Contains(optionName))
                continue;

            int? valueIndex = null;
            if (!HasInlineOptionValue(token) && index + 1 < tokens.Count && !IsOptionToken(tokens[index + 1]))
            {
                valueIndex = index + 1;
                index++;
            }

            if (lastOptionIndexes.TryGetValue(optionName, out var previous))
            {
                keep[previous.OptionIndex] = false;
                if (previous.ValueIndex is int previousValueIndex)
                    keep[previousValueIndex] = false;
            }

            var optionIndex = valueIndex is null ? index : index - 1;
            lastOptionIndexes[optionName] = (optionIndex, valueIndex);
        }

        return string.Join(
            " ",
            tokens
                .Where((_, index) => keep[index])
                .Select(QuoteCommandLineToken));
    }

    private static JsonArray NormalizeGameArgumentArray(JsonArray arguments)
    {
        var normalized = new JsonArray();
        var keep = Enumerable.Repeat(true, arguments.Count).ToArray();
        var lastOptionIndexes = new Dictionary<string, (int OptionIndex, int? ValueIndex)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < arguments.Count; index++)
        {
            var token = GetStringValue(arguments[index]);
            if (token is null)
                continue;

            var optionName = GetOptionName(token);
            if (optionName is null || !SingleValueMinecraftArgumentOptions.Contains(optionName))
                continue;

            int? valueIndex = null;
            if (!HasInlineOptionValue(token)
                && index + 1 < arguments.Count
                && GetStringValue(arguments[index + 1]) is { } value
                && !IsOptionToken(value))
            {
                valueIndex = index + 1;
                index++;
            }

            if (lastOptionIndexes.TryGetValue(optionName, out var previous))
            {
                keep[previous.OptionIndex] = false;
                if (previous.ValueIndex is int previousValueIndex)
                    keep[previousValueIndex] = false;
            }

            var optionIndex = valueIndex is null ? index : index - 1;
            lastOptionIndexes[optionName] = (optionIndex, valueIndex);
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            if (keep[index])
                normalized.Add(arguments[index]?.DeepClone());
        }

        return normalized;
    }

    private static string? GetStringValue(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : null;
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        // 只实现版本元数据所需的引号和转义子集，避免含空格参数被粗暴拆散。
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AppendCurrentToken();
                continue;
            }

            if (character == '\\' && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
            {
                current.Append('"');
                index++;
                continue;
            }

            current.Append(character);
        }

        AppendCurrentToken();
        return tokens;

        void AppendCurrentToken()
        {
            if (current.Length == 0)
                return;

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static string? GetOptionName(string token)
    {
        if (!IsOptionToken(token))
            return null;

        var separatorIndex = token.IndexOf('=');
        return separatorIndex > 0 ? token[..separatorIndex] : token;
    }

    private static bool HasInlineOptionValue(string token)
    {
        var separatorIndex = token.IndexOf('=');
        return separatorIndex > 0 && separatorIndex < token.Length - 1;
    }

    private static bool IsOptionToken(string token)
    {
        return token.StartsWith("--", StringComparison.Ordinal);
    }

    private static string QuoteCommandLineToken(string token)
    {
        if (token.Length == 0)
            return "\"\"";

        return token.Any(char.IsWhiteSpace) || token.Contains('"')
            ? $"\"{token.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : token;
    }
}
