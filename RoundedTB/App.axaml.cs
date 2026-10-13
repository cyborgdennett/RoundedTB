using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;

namespace RoundedTB;

public partial class App : Application
{
    public static MainWindow Main { get; private set; }
    public override void Initialize()
    {
        StartupDiagnostics.Mark("application-resources-start");
        AvaloniaXamlLoader.Load(this);
        StartupDiagnostics.Mark("application-resources-loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Main = new MainWindow();
            Main.Closed += (_, _) => desktop.Shutdown();
            if (Array.IndexOf(desktop.Args ?? Array.Empty<string>(), "--startup") >= 0)
            {
                // Leave MainWindow unset during automatic lifetime startup, which
                // otherwise calls Show(). The native window already exists while hidden.
                Dispatcher.UIThread.Post(async () =>
                {
                    desktop.MainWindow = Main;
                    await Main.StartAsync();
                });
            }
            else desktop.MainWindow = Main;
            desktop.ShutdownRequested += (_, e) =>
            {
                if (Main.shouldReallyDieNoReally) return;
                e.Cancel = true;
                Main.Exit();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
