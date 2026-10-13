using System;
using Avalonia;
using System.Threading;
using System.IO;

namespace RoundedTB;

internal static class Program
{
    public static uint ShowSettingsMessage { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("RoundedTB requires Windows 10 or later to modify Explorer's taskbar.");
            return 1;
        }
        if (args.Length == 3 && args[0] == "--recover-taskbar")
            return TaskbarRecovery.Run(args[1], args[2]);
        StartupDiagnostics.Start(Array.IndexOf(args, "--startup") >= 0);
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            try
            {
                var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rtb.log");
                File.AppendAllText(log, $"[{DateTime.Now:O}] Unhandled exception: {error.ExceptionObject}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        ShowSettingsMessage = (uint)LocalPInvoke.RegisterWindowMessage("RoundedTB.ShowSettings");
        using var instance = new Mutex(true, @"Local\RoundedTB", out var firstInstance);
        if (!firstInstance)
        {
            StartupDiagnostics.Mark("existing-instance");
            if (Array.IndexOf(args, "--startup") < 0)
                LocalPInvoke.PostMessage(new IntPtr(0xffff), ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        TaskbarRecovery.Start();
        if (Array.IndexOf(args, "--startup") >= 0)
        {
            try { StartupRegistration.SetRestartRegistration(true); }
            catch (Exception error) { StartupDiagnostics.Mark("windows-restart-registration-failed-" + error.HResult); }
        }
        StartupDiagnostics.Mark("avalonia-start");
        var app = BuildAvaloniaApp();
        StartupDiagnostics.Mark("avalonia-builder-ready");
        return app.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect()
            // This settings utility does not need a GPU device or ANGLE shaders.
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            .LogToTrace();
}
