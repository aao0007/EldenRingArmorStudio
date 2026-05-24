using EldenRingArmorStudio.Core;
using Serilog;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EldenRingArmorStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("data/logs/studio_.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} | {Level:u3} | {SourceContext} | {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Iniciando Elden Ring Armor Studio...");
        AppConfig.Instance.Load();

        base.OnStartup(e);

        // Propagar el foreground correcto a toda la jerarquía visual,
        // incluyendo los paneles de AvalonDock que no respetan DynamicResource
        ThemeManager.LoadSavedOrDefault();
        ApplyForegroundToAllWindows();
    }

    /// <summary>
    /// Fuerza TextElement.Foreground en cada Window para que los controles
    /// dentro de AvalonDock hereden el color de texto del tema activo.
    /// Se llama también desde ThemeManager.Apply() al hacer toggle.
    /// </summary>
    public static void ApplyForegroundToAllWindows()
    {
        if (Current?.Resources == null) return;

        // Leer el color del tema actual
        var brush = Current.Resources["TextPrimary"] as SolidColorBrush
                    ?? Brushes.White;

        foreach (Window w in Current.Windows)
        {
            // TextElement.Foreground en cascada afecta a todos los hijos
            TextElement.SetForeground(w, brush);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}