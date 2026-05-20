using Serilog;
using System.Windows;

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}