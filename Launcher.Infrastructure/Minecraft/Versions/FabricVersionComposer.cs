using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class FabricVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
        string finalVersionName,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var finalVersionDirectory = Path.Combine(minecraftDirectory, "versions", finalVersionName);
        var finalVersionJsonPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.json");
        var finalVersionJarPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.jar");

        if (Directory.Exists(finalVersionDirectory))
            throw new IOException($"Version directory already exists: {finalVersionName}");

        var baseVersionJson = await DownloadBaseVersionJsonAsync(httpClient, minecraftVersion, cancellationToken);
        var fabricProfileJson = await DownloadFabricProfileJsonAsync(httpClient, minecraftVersion, loaderVersion, cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, fabricProfileJson, finalVersionName);

        Directory.CreateDirectory(finalVersionDirectory);

        try
        {
            await File.WriteAllTextAsync(
                finalVersionJsonPath,
                finalVersionJson.ToJsonString(JsonOptions),
                cancellationToken);

            await DownloadClientJarAsync(httpClient, baseVersionJson, finalVersionJarPath, cancellationToken);
        }
        catch
        {
            if (Directory.Exists(finalVersionDirectory))
                Directory.Delete(finalVersionDirectory, recursive: true);

            throw;
        }

        return finalVersionName;
    }

    internal static JsonObject BuildFinalVersionJson(
        JsonObject baseVersionJson,
        JsonObject fabricProfileJson,
        string finalVersionName)
    {
        var mergedVersion = (JsonObject)baseVersionJson.DeepClone();

        foreach (var property in fabricProfileJson)
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

        mergedVersion["id"] = finalVersionName;
        mergedVersion["jar"] = finalVersionName;
        mergedVersion.Remove("inheritsFrom");
        return mergedVersion;
    }

    private static async Task<JsonObject> DownloadBaseVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        using var manifestStream = await httpClient.GetStreamAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            cancellationToken);
        var manifestNode = await JsonNode.ParseAsync(manifestStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Minecraft version manifest is empty.");

        var versionEntries = manifestNode["versions"]?.AsArray()
            ?? throw new InvalidDataException("Minecraft version manifest is missing versions.");
        var versionUrl = versionEntries
            .Select(entry => entry?.AsObject())
            .FirstOrDefault(entry =>
                string.Equals(entry?["id"]?.GetValue<string>(), minecraftVersion, StringComparison.OrdinalIgnoreCase))?["url"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(versionUrl))
            throw new InvalidOperationException($"Minecraft version metadata not found: {minecraftVersion}");

        using var versionStream = await httpClient.GetStreamAsync(versionUrl, cancellationToken);
        var versionNode = await JsonNode.ParseAsync(versionStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Minecraft version metadata is empty: {minecraftVersion}");
        return versionNode.AsObject();
    }

    private static async Task<JsonObject> DownloadFabricProfileJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
        using var profileStream = await httpClient.GetStreamAsync(profileUrl, cancellationToken);
        var profileNode = await JsonNode.ParseAsync(profileStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Fabric profile metadata is empty: {minecraftVersion} {loaderVersion}");
        return profileNode.AsObject();
    }

    private static async Task DownloadClientJarAsync(
        HttpClient httpClient,
        JsonObject baseVersionJson,
        string destinationJarPath,
        CancellationToken cancellationToken)
    {
        var clientUrl = baseVersionJson["downloads"]?["client"]?["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("Minecraft version metadata is missing downloads.client.url.");

        await using var jarStream = await httpClient.GetStreamAsync(clientUrl, cancellationToken);
        await using var destinationStream = File.Create(destinationJarPath);
        await jarStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private static JsonArray MergeLibraries(JsonArray? baseLibraries, JsonArray? derivedLibraries)
    {
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

    private static JsonObject MergeArguments(JsonObject? baseArguments, JsonObject? derivedArguments)
    {
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

                mergedArguments[property.Key] = mergedArray;
                continue;
            }

            mergedArguments[property.Key] = property.Value?.DeepClone();
        }

        return mergedArguments;
    }

    private static string MergeMinecraftArguments(string? baseArguments, string? derivedArguments)
    {
        if (string.IsNullOrWhiteSpace(baseArguments))
            return derivedArguments ?? string.Empty;

        if (string.IsNullOrWhiteSpace(derivedArguments))
            return baseArguments;

        return $"{baseArguments} {derivedArguments}";
    }
}
