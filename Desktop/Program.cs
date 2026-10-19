using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace SilverpineSilcEditor.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<EditorApp>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public sealed class EditorApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Color.Parse("#79DAB9");
        Resources["SystemAccentColorDark1"] = Color.Parse("#459C85");
        Resources["SystemAccentColorLight1"] = Color.Parse("#A7E9D3");
        Resources["ControlCornerRadius"] = new CornerRadius(6);
        Styles.Add(new Style(s => s.OfType<Avalonia.Controls.TabItem>())
        {
            Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 14d),
                new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(12, 10)) }
        });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new EditorWindow(desktop.Args?.FirstOrDefault());
        base.OnFrameworkInitializationCompleted();
    }
}
