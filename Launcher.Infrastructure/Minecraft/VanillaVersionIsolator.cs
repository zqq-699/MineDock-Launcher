using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionIsolator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateIsolatedVersionAsync(
        string minecraftVersion,
        string isolatedVersionName,
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var versionName = string.IsNullOrWhiteSpace(isolatedVersionName)
            ? minecraftVersion
            : isolatedVersionName.Trim();

        if (string.Equals(versionName, minecraftVersion, StringComparison.OrdinalIgnoreCase))
            return minecraftVersion;

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var sourceDirectory = Path.Combine(versionsDirectory, minecraftVersion);
        var destinationDirectory = Path.Combine(versionsDirectory, versionName);
        var sourceJsonPath = Path.Combine(sourceDirectory, $"{minecraftVersion}.json");
        var sourceJarPath = Path.Combine(sourceDirectory, $"{minecraftVersion}.jar");
        var destinationJsonPath = Path.Combine(destinationDirectory, $"{versionName}.json");
        var destinationJarPath = Path.Combine(destinationDirectory, $"{versionName}.jar");

        if (!File.Exists(sourceJsonPath))
            throw new FileNotFoundException($"原版版本 JSON 不存在：{sourceJsonPath}", sourceJsonPath);

        if (!File.Exists(sourceJarPath))
            throw new FileNotFoundException($"原版客户端文件不存在：{sourceJarPath}", sourceJarPath);

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"已存在同名版本文件夹：{versionName}");

        Directory.CreateDirectory(destinationDirectory);

        try
        {
            File.Copy(sourceJarPath, destinationJarPath, overwrite: false);

            await using var sourceJsonStream = File.OpenRead(sourceJsonPath);
            var versionJson = await JsonNode.ParseAsync(sourceJsonStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException($"版本 JSON 为空：{sourceJsonPath}");

            var versionObject = versionJson.AsObject();
            versionObject["id"] = versionName;
            versionObject["jar"] = versionName;

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
}
