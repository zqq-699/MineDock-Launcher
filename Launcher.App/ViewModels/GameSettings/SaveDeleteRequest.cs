namespace Launcher.App.ViewModels.GameSettings;

public sealed record SaveDeleteRequest(
    IReadOnlyList<string> FullPaths,
    IReadOnlyList<string> Titles);
