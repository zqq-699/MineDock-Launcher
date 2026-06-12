namespace Launcher.Core.Models;

public sealed record LauncherProgress(string Stage, string Message, double? Percent = null);
