using Xunit;

namespace Orderly.Tests.Settings;

public sealed class StartupSectionRuntimeWiringTests
{
    [Fact]
    public void Main_load_synchronizes_settings_child_viewmodel_from_loaded_preferences()
    {
        var source = LoadSource("src", "Orderly.App", "ViewModels", "MainViewModel.Loading.cs");

        Assert.Contains("ApplySettingsInputsFromPreferences(Preferences);", source);
        Assert.Contains("Settings.ApplySettingsInputsFromPreferences(Preferences);", source);
        Assert.True(
            source.IndexOf("ApplySettingsInputsFromPreferences(Preferences);", StringComparison.Ordinal)
            < source.IndexOf("ApplyStartupSectionPreferenceIfNeeded();", StringComparison.Ordinal),
            "启动页应用前必须先用同一份 Preferences 同步壳层设置输入。");
        Assert.True(
            source.IndexOf("Settings.ApplySettingsInputsFromPreferences(Preferences);", StringComparison.Ordinal)
            < source.IndexOf("ApplyStartupSectionPreferenceIfNeeded();", StringComparison.Ordinal),
            "启动页应用前必须先同步 SettingsViewModel 基线，避免后续保存用旧基线覆盖。");
    }

    [Fact]
    public void Startup_default_selection_uses_single_runtime_settings_source()
    {
        var source = LoadSource("src", "Orderly.App", "Views", "MainWindow.xaml.cs");

        Assert.Contains("mainVM.StartupDefaultSectionInput = sectionName;", source);
        Assert.DoesNotContain("mainVM.Settings.StartupDefaultSectionInput = sectionName;", source);
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
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orderly.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "无法通过向上查找 Orderly.sln 定位仓库根目录，起点：" + AppContext.BaseDirectory + "。");
    }
}
