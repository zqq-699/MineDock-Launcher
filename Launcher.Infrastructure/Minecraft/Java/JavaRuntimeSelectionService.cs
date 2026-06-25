using System.IO;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

public sealed class JavaRuntimeSelectionService : IJavaRuntimeSelectionService
{
    private readonly IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService;

    public JavaRuntimeSelectionService(IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService)
    {
        this.javaRuntimeDiscoveryService = javaRuntimeDiscoveryService;
    }

    public async Task<JavaRuntimeInfo> SelectForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        var effectiveSelection = ResolveEffectiveSelection(instance, settings);
        if (effectiveSelection.Mode is JavaSelectionMode.Manual)
            return await SelectManualRuntimeAsync(effectiveSelection.ExecutablePath, cancellationToken);

        return await SelectAutomaticRuntimeAsync(instance, settings, cancellationToken);
    }

    internal static EffectiveJavaSelection ResolveEffectiveSelection(GameInstance instance, LauncherSettings settings)
    {
        return instance.JavaSettingsMode is LaunchSettingsMode.PerInstance
            ? new EffectiveJavaSelection(instance.JavaSelectionMode, instance.SelectedJavaExecutablePath)
            : new EffectiveJavaSelection(settings.JavaSelectionMode, settings.SelectedJavaExecutablePath);
    }

    internal static int? ResolveRequiredMajorVersion(
        GameInstance instance,
        LauncherSettings settings,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null)
    {
        fileExists ??= File.Exists;
        readAllText ??= File.ReadAllText;

        foreach (var versionJsonPath in EnumerateVersionJsonPaths(instance, settings))
        {
            if (!fileExists(versionJsonPath))
                continue;

            var requiredMajorVersion = TryReadJavaMajorVersion(versionJsonPath, readAllText);
            if (requiredMajorVersion is not null)
                return requiredMajorVersion;
        }

        var minecraftVersion = ResolveMinecraftVersion(instance, settings, fileExists, readAllText);
        return GuessRequiredMajorVersion(minecraftVersion);
    }

    internal static JavaRuntimeInfo? SelectBestRuntime(
        IReadOnlyList<JavaRuntimeInfo> runtimes,
        int? requiredMajorVersion)
    {
        if (runtimes.Count == 0)
            return null;

        if (requiredMajorVersion is not int required)
            return runtimes
                .OrderByDescending(IsX64)
                .ThenByDescending(runtime => runtime.MajorVersion ?? 0)
                .First();

        var knownMajorRuntimes = runtimes
            .Where(runtime => runtime.MajorVersion is not null)
            .ToList();

        if (knownMajorRuntimes.Count == 0)
            return null;

        return knownMajorRuntimes
            .Where(runtime => runtime.MajorVersion!.Value >= required)
            .Select(runtime => new
            {
                Runtime = runtime,
                Tier = GetCompatibilityTier(runtime.MajorVersion!.Value, required),
                Distance = GetCompatibilityDistance(runtime.MajorVersion!.Value, required)
            })
            .OrderBy(item => item.Tier)
            .ThenBy(item => item.Distance)
            .ThenByDescending(item => IsX64(item.Runtime))
            .ThenByDescending(item => item.Runtime.MajorVersion)
            .Select(item => item.Runtime)
            .FirstOrDefault();
    }

    internal static int? GuessRequiredMajorVersion(string? minecraftVersion)
    {
        if (!TryParseMinecraftVersion(minecraftVersion, out var major, out var minor, out var patch))
            return null;

        if (major > 1 || major == 1 && (minor > 20 || minor == 20 && patch >= 5))
            return 21;

        if (major == 1 && minor >= 18)
            return 17;

        if (major == 1 && minor >= 17)
            return 16;

        return 8;
    }

    private async Task<JavaRuntimeInfo> SelectManualRuntimeAsync(
        string? executablePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new JavaRuntimeSelectionException(
                "No Java runtime is selected for manual Java mode.",
                JavaRuntimeSelectionFailureReason.ManualRuntimeMissing);
        }

        try
        {
            return await javaRuntimeDiscoveryService.DiscoverExecutableAsync(
                executablePath,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new JavaRuntimeSelectionException(
                "The selected Java runtime is not available.",
                exception,
                JavaRuntimeSelectionFailureReason.ManualRuntimeUnavailable);
        }
    }

    private async Task<JavaRuntimeInfo> SelectAutomaticRuntimeAsync(
        GameInstance instance,
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        var runtimes = await javaRuntimeDiscoveryService.DiscoverAsync(
            settings.MinecraftDirectory,
            cancellationToken);
        var requiredMajorVersion = ResolveRequiredMajorVersion(instance, settings);
        if (runtimes.Count == 0)
        {
            var missingRequirementText = requiredMajorVersion is int missingRequired
                ? $"Java {missingRequired} or newer"
                : "a usable Java runtime";
            throw new JavaRuntimeSelectionException(
                $"No {missingRequirementText} was found.",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                requiredMajorVersion);
        }

        var selectedRuntime = SelectBestRuntime(runtimes, requiredMajorVersion);

        if (selectedRuntime is not null)
            return selectedRuntime;

        var requirementText = requiredMajorVersion is int required
            ? $"Java {required} or newer"
            : "a usable Java runtime";
        var discoveredVersions = string.Join(
            ", ",
            runtimes
                .Select(runtime => runtime.MajorVersion?.ToString() ?? "unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(version => version));

        throw new JavaRuntimeSelectionException(
            string.IsNullOrWhiteSpace(discoveredVersions)
                ? $"No {requirementText} was found."
                : $"No {requirementText} was found. Discovered Java versions: {discoveredVersions}.",
            JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound,
            requiredMajorVersion);
    }

    private static IEnumerable<string> EnumerateVersionJsonPaths(GameInstance instance, LauncherSettings settings)
    {
        var versionName = string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;

        if (!string.IsNullOrWhiteSpace(instance.InstanceDirectory) && !string.IsNullOrWhiteSpace(versionName))
            yield return Path.Combine(instance.InstanceDirectory, $"{versionName}.json");

        if (!string.IsNullOrWhiteSpace(settings.MinecraftDirectory) && !string.IsNullOrWhiteSpace(versionName))
            yield return Path.Combine(settings.MinecraftDirectory, "versions", versionName, $"{versionName}.json");
    }

    private static int? TryReadJavaMajorVersion(
        string versionJsonPath,
        Func<string, string> readAllText)
    {
        try
        {
            using var document = JsonDocument.Parse(readAllText(versionJsonPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("javaVersion", out var javaVersion)
                || javaVersion.ValueKind is not JsonValueKind.Object
                || !javaVersion.TryGetProperty("majorVersion", out var majorVersion))
            {
                return null;
            }

            return majorVersion.ValueKind is JsonValueKind.Number
                   && majorVersion.TryGetInt32(out var number)
                ? number
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveMinecraftVersion(
        GameInstance instance,
        LauncherSettings settings,
        Func<string, bool> fileExists,
        Func<string, string> readAllText)
    {
        if (!string.IsNullOrWhiteSpace(instance.MinecraftVersion))
            return instance.MinecraftVersion;

        foreach (var versionJsonPath in EnumerateVersionJsonPaths(instance, settings))
        {
            if (!fileExists(versionJsonPath))
                continue;

            var metadataVersion = TryReadLauncherMinecraftVersion(versionJsonPath, readAllText);
            if (!string.IsNullOrWhiteSpace(metadataVersion))
                return metadataVersion;
        }

        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? string.Empty
            : instance.VersionName;
    }

    private static string TryReadLauncherMinecraftVersion(
        string versionJsonPath,
        Func<string, string> readAllText)
    {
        try
        {
            using var document = JsonDocument.Parse(readAllText(versionJsonPath));
            return LauncherVersionMetadata.ReadMinecraftVersion(document.RootElement);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryParseMinecraftVersion(
        string? version,
        out int major,
        out int minor,
        out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        var numericPart = version.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = numericPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], out major)
            || !int.TryParse(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length >= 3)
            _ = int.TryParse(parts[2], out patch);

        return true;
    }

    private static int GetCompatibilityTier(int runtimeMajorVersion, int requiredMajorVersion)
    {
        if (runtimeMajorVersion == requiredMajorVersion)
            return 0;

        return runtimeMajorVersion > requiredMajorVersion ? 1 : 2;
    }

    private static int GetCompatibilityDistance(int runtimeMajorVersion, int requiredMajorVersion)
    {
        return Math.Abs(runtimeMajorVersion - requiredMajorVersion);
    }

    private static bool IsX64(JavaRuntimeInfo runtime)
    {
        return string.Equals(runtime.Architecture, "x64", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record EffectiveJavaSelection(JavaSelectionMode Mode, string? ExecutablePath);
}
