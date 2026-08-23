using Avalonia;

namespace PrinterRescue.Gui;

/// <summary>Aplicativo Avalonia. UI completa entregue pelo agente B3.</summary>
public class App : Application
{
    public override void Initialize()
    {
    }
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
