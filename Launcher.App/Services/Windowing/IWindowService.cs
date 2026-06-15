using System.Windows;

namespace Launcher.App.Services;

public interface IWindowService
{
    void Attach(Window window);

    void Minimize();

    void Close();
}
