namespace Launcher.Domain.Models;

public sealed class LocalMod
{
    public string Name { get; set; } = string.Empty;
    public string? Loader { get; set; }
    public string? ModId { get; set; }
    public string? Version { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? IconSource { get; set; }
    public bool IsEnabled { get; set; }
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "Local";
}
