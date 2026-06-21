namespace Launcher.App.ViewModels.GameSettings;

public readonly record struct GameSettingsFileDropEvaluation(
    bool ShouldHandle,
    bool CanAccept,
    string Message)
{
    public static GameSettingsFileDropEvaluation Hidden => new(false, false, string.Empty);

    public static GameSettingsFileDropEvaluation Accept(string message) => new(true, true, message);

    public static GameSettingsFileDropEvaluation Reject(string message) => new(true, false, message);
}
