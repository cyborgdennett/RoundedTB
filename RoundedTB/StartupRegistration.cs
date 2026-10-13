using System;
using System.IO;
using Microsoft.Win32;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace RoundedTB;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string LegacyLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "RoundedTB.lnk");

    private static string TaskName => "RoundedTB-" + WindowsIdentity.GetCurrent().User.Value;
    private static dynamic Scheduler()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();
        return service;
    }

    public static void SetRestartRegistration(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Match Chrome: restore on Windows restart, without crash/hang/update loops.
        var result = enabled ? RegisterApplicationRestart("--startup", 1 | 2 | 4)
            : UnregisterApplicationRestart();
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        StartupDiagnostics.Mark(enabled ? "windows-restart-registered" : "windows-restart-unregistered");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, uint flags);
    [DllImport("kernel32.dll")]
    private static extern int UnregisterApplicationRestart();

    public static bool IsRegistered()
    {
        try
        {
            dynamic task = Scheduler().GetFolder("\\").GetTask(TaskName);
            return task.Enabled;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("RoundedTB") is string || File.Exists(LegacyLink);
    }

    public static void Enable()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "RoundedTB.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Startup requires the published Windows executable.", exe);
        var sid = WindowsIdentity.GetCurrent().User.Value;
        dynamic service = Scheduler();
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Start RoundedTB when this user signs in.";
        definition.Principal.UserId = sid;
        definition.Principal.LogonType = 3; // Interactive token, no password.
        definition.Principal.RunLevel = 0;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = 2; // Ignore a duplicate launch.
        dynamic trigger = definition.Triggers.Create(9); // At logon.
        trigger.UserId = sid;
        trigger.Delay = "PT0S";
        dynamic action = definition.Actions.Create(0);
        action.Path = exe;
        action.Arguments = "--startup";
        action.WorkingDirectory = AppContext.BaseDirectory;
        service.GetFolder("\\").RegisterTaskDefinition(TaskName, definition, 6, sid, null, 3);
        RemoveLegacy();
        SetRestartRegistration(true);
    }

    private static void RemoveLegacy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue("RoundedTB", false);
        File.Delete(LegacyLink);
    }

    public static void Disable()
    {
        try { Scheduler().GetFolder("\\").DeleteTask(TaskName, 0); }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { }
        RemoveLegacy();
        SetRestartRegistration(false);
    }
}
