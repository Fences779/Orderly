using Velopack;

namespace Orderly.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var isVelopackCommand = Array.Exists(
            args,
            arg => arg.StartsWith("--velo", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("/velo", StringComparison.OrdinalIgnoreCase));

        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .Run();

        if (isVelopackCommand)
        {
            return;
        }

        App app = new();
        app.InitializeComponent();
        app.Run();
    }
}
