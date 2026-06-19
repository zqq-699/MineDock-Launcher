namespace Launcher.Application.Services;

public interface ISystemMemoryService
{
    SystemMemorySnapshot GetSnapshot();
}
