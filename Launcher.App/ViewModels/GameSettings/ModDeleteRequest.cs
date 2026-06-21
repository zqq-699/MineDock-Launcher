namespace Launcher.App.ViewModels.GameSettings;

public sealed record ModDeleteRequest(
    IReadOnlyList<string> FullPaths,
    IReadOnlyList<string> Titles);
