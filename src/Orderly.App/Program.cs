using Velopack;

namespace Orderly.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }
}
