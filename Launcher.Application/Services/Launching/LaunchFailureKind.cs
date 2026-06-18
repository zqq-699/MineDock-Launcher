namespace Launcher.Application.Services;

public enum LaunchFailureKind
{
    StartupFailed,
    StartupProcessExited,
    StartupAbnormalExit,
    RuntimeAbnormalExit
}
