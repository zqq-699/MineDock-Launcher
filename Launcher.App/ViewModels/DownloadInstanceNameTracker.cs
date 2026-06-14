using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

internal sealed class DownloadInstanceNameTracker
{
    private readonly object syncRoot = new();
    private readonly HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingNames = new(StringComparer.OrdinalIgnoreCase);

    public void ReplaceExisting(IEnumerable<GameInstance> instances)
    {
        lock (syncRoot)
        {
            existingNames.Clear();
            foreach (var instance in instances)
            {
                AddNormalized(existingNames, instance.Name);
                AddNormalized(existingNames, instance.VersionName);
            }
        }
    }

    public void AddExisting(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            existingNames.Add(normalized);
        }
    }

    public void AddPending(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            pendingNames.Add(normalized);
        }
    }

    public void RemovePending(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        lock (syncRoot)
        {
            pendingNames.Remove(normalized);
        }
    }

    public bool IsUnavailable(string? name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        lock (syncRoot)
        {
            return existingNames.Contains(normalized) || pendingNames.Contains(normalized);
        }
    }

    private static void AddNormalized(ISet<string> names, string? name)
    {
        var normalized = Normalize(name);
        if (!string.IsNullOrWhiteSpace(normalized))
            names.Add(normalized);
    }

    private static string Normalize(string? name)
    {
        return name?.Trim() ?? string.Empty;
    }
}
