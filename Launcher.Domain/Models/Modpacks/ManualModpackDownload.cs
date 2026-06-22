namespace Launcher.Domain.Models;

public sealed class ManualModpackDownload
{
    public long? ProjectId { get; init; }

    public long? FileId { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string SuggestedUrl { get; init; } = string.Empty;

    public string FailureSummary { get; init; } = string.Empty;
}
