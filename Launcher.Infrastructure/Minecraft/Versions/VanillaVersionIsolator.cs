using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionIsolator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Task<string> CreateIsolatedVersionAsync(
        string minecraftVersion,
        string isolatedVersionName,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateIsolatedVersionFromSourceAsync(
            minecraftVersion,
            isolatedVersionName,
            minecraftDirectory,
            requireJarCopy: true,
            cancellationToken);
    }

    public static async Task<string> CreateIsolatedVersionFromSourceAsync(
        string sourceVersionName,
        string isolatedVersionName,
        string minecraftDirectory,
        bool requireJarCopy = false,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(isolatedVersionName)
            ? sourceVersionName
            : isolatedVersionName.Trim();

        if (string.Equals(versionName, sourceVersionName, StringComparison.OrdinalIgnoreCase))
            return sourceVersionName;

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var sourceDirectory = Path.Combine(versionsDirectory, sourceVersionName);
        var destinationDirectory = Path.Combine(versionsDirectory, versionName);
        var sourceJsonPath = Path.Combine(sourceDirectory, $"{sourceVersionName}.json");
        var sourceJarPath = Path.Combine(sourceDirectory, $"{sourceVersionName}.jar");
        var destinationJsonPath = Path.Combine(destinationDirectory, $"{versionName}.json");
        var destinationJarPath = Path.Combine(destinationDirectory, $"{versionName}.jar");

        if (!File.Exists(sourceJsonPath))
            throw new FileNotFoundException($"Version JSON not found: {sourceJsonPath}", sourceJsonPath);

        if (requireJarCopy && !File.Exists(sourceJarPath))
            throw new FileNotFoundException($"Version jar not found: {sourceJarPath}", sourceJarPath);

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {versionName}");

        Directory.CreateDirectory(destinationDirectory);

        try
        {
            var hasSourceJar = File.Exists(sourceJarPath);
            if (hasSourceJar)
                File.Copy(sourceJarPath, destinationJarPath, overwrite: false);

            CopySupplementaryFiles(
                sourceDirectory,
                destinationDirectory,
                excludedFileNames:
                [
                    $"{sourceVersionName}.json",
                    $"{sourceVersionName}.jar"
                ]);

            await using var sourceJsonStream = File.OpenRead(sourceJsonPath);
            var versionJson = await JsonNode.ParseAsync(sourceJsonStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException($"Version JSON is empty: {sourceJsonPath}");

            var versionObject = versionJson.AsObject();
            versionObject["id"] = versionName;

            if (hasSourceJar)
                versionObject["jar"] = versionName;
            else
                versionObject.Remove("jar");

            await File.WriteAllTextAsync(
                destinationJsonPath,
                versionObject.ToJsonString(JsonOptions),
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);

            throw;
        }

        return versionName;
    }

    public static async Task<string> CreateFlattenedDerivedVersionAsync(
        string baseVersionName,
        string derivedVersionName,
        string isolatedVersionName,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(isolatedVersionName)
            ? derivedVersionName
            : isolatedVersionName.Trim();

        if (string.Equals(versionName, derivedVersionName, StringComparison.OrdinalIgnoreCase))
            return derivedVersionName;

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var baseDirectory = Path.Combine(versionsDirectory, baseVersionName);
        var derivedDirectory = Path.Combine(versionsDirectory, derivedVersionName);
        var destinationDirectory = Path.Combine(versionsDirectory, versionName);

        var baseJsonPath = Path.Combine(baseDirectory, $"{baseVersionName}.json");
        var baseJarPath = Path.Combine(baseDirectory, $"{baseVersionName}.jar");
        var derivedJsonPath = Path.Combine(derivedDirectory, $"{derivedVersionName}.json");
        var destinationJsonPath = Path.Combine(destinationDirectory, $"{versionName}.json");
        var destinationJarPath = Path.Combine(destinationDirectory, $"{versionName}.jar");

        if (!File.Exists(baseJsonPath))
            throw new FileNotFoundException($"Base version JSON not found: {baseJsonPath}", baseJsonPath);

        if (!File.Exists(baseJarPath))
            throw new FileNotFoundException($"Base version jar not found: {baseJarPath}", baseJarPath);

        if (!File.Exists(derivedJsonPath))
            throw new FileNotFoundException($"Derived version JSON not found: {derivedJsonPath}", derivedJsonPath);

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {versionName}");

        Directory.CreateDirectory(destinationDirectory);

        try
        {
            File.Copy(baseJarPath, destinationJarPath, overwrite: false);
            CopySupplementaryFiles(
                derivedDirectory,
                destinationDirectory,
                excludedFileNames:
                [
                    $"{derivedVersionName}.json",
                    $"{derivedVersionName}.jar"
                ]);

            var baseVersionObject = await ReadJsonObjectAsync(baseJsonPath, cancellationToken);
            var derivedVersionObject = await ReadJsonObjectAsync(derivedJsonPath, cancellationToken);
            var flattenedVersion = MergeFlattenedVersionJson(baseVersionObject, derivedVersionObject, versionName);

            await File.WriteAllTextAsync(
                destinationJsonPath,
                flattenedVersion.ToJsonString(JsonOptions),
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(destinationDirectory))
                Directory.Delete(destinationDirectory, recursive: true);

            throw;
        }

        return versionName;
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(string jsonPath, CancellationToken cancellationToken)
    {
        await using var jsonStream = File.OpenRead(jsonPath);
        var jsonNode = await JsonNode.ParseAsync(jsonStream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"Version JSON is empty: {jsonPath}");
        return jsonNode.AsObject();
    }

    private static JsonObject MergeFlattenedVersionJson(
        JsonObject baseVersion,
        JsonObject derivedVersion,
        string versionName)
    {
        return VersionJsonMergeHelper.MergeFlattenedVersion(baseVersion, derivedVersion, versionName);
    }

    private static void CopySupplementaryFiles(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> excludedFileNames)
    {
        var excluded = new HashSet<string>(excludedFileNames, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFilePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(sourceFilePath);
            if (excluded.Contains(fileName))
                continue;

            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourceFilePath, destinationPath, overwrite: false);
        }
    }
}
