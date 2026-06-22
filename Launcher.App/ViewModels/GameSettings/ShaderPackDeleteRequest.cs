namespace Launcher.App.ViewModels.GameSettings;

public sealed record ShaderPackDeleteRequest(
    IReadOnlyList<string> FullPaths,
    IReadOnlyList<string> Titles);
