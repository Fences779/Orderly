using Xunit;

namespace Orderly.Tests.Settings;

public sealed class WindowCloseRuntimeWiringTests
{
    [Fact]
    public void Main_window_close_respects_minimize_to_tray_setting()
    {
        var source = LoadSource("src", "Orderly.App", "Views", "MainWindow.xaml.cs");

        Assert.Contains("if (_viewModel.StartMinimizedToTrayInput)", source);
        Assert.Contains("HiddenToTray?.Invoke(this, EventArgs.Empty);", source);
        Assert.Contains("ExitRequested?.Invoke(this, EventArgs.Empty);", source);
    }

    [Fact]
    public void Main_window_exit_request_uses_the_centralized_cleanup_path()
    {
        var compositionSource = LoadSource("src", "Orderly.App", "App.WorkspaceComposition.cs");
        var appSource = LoadSource("src", "Orderly.App", "App.xaml.cs");

        Assert.Contains("_mainWindow.ExitRequested += OnMainWindowExitRequested;", compositionSource);
        Assert.Contains("Dispatcher.InvokeAsync(() => ExitApplication())", appSource);
        Assert.Contains("_mainWindow.ExitRequested -= OnMainWindowExitRequested;", appSource);
    }

    [Fact]
    public void Floating_window_exit_uses_the_same_cleanup_path_as_the_tray_exit()
    {
        var xamlSource = LoadSource("src", "Orderly.App", "Views", "FloatingWindow.xaml");
        var windowSource = LoadSource("src", "Orderly.App", "Views", "FloatingWindow.xaml.cs");
        var compositionSource = LoadSource("src", "Orderly.App", "App.WorkspaceComposition.cs");

        Assert.Contains("Header=\"退出\" Click=\"MenuExit_Click\"", xamlSource);
        Assert.Contains("_exitApplication();", windowSource);
        Assert.Contains("ExitApplicationFromTray);", compositionSource);
    }

    private static string LoadSource(params string[] segments)
    {
        var path = ResolveRepositoryRoot();
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        Assert.True(File.Exists(path), $"未找到源码文件：{path}");
        return File.ReadAllText(path);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orderly.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "无法通过向上查找 Orderly.sln 定位仓库根目录，起点：" + AppContext.BaseDirectory + "。");
    }
}
