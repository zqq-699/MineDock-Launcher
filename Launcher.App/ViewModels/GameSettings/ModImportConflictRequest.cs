namespace Launcher.App.ViewModels.GameSettings;

public sealed record ModImportConflictRequest(
    string SourcePath,
    string FileName);
