namespace Launcher.Domain.Models;

public sealed class LocalMod
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "Local";
}
