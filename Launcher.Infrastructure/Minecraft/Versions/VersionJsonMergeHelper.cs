using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class VersionJsonMergeHelper
{
    public static JsonObject MergeFlattenedVersion(
        JsonObject baseVersion,
        JsonObject derivedVersion,
        string versionName,
        string? minecraftVersion = null)
    {
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

    public static string MergeMinecraftArguments(string? baseArguments, string? derivedArguments)
    {
        if (string.IsNullOrWhiteSpace(baseArguments))
            return derivedArguments ?? string.Empty;

        if (string.IsNullOrWhiteSpace(derivedArguments))
            return baseArguments;

        return $"{baseArguments} {derivedArguments}";
    }
}
