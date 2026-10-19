using SilverpineSilcEditor.UI;

namespace SilverpineSilcEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm(args.FirstOrDefault()));
    }
}
