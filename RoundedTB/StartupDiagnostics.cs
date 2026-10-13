using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace RoundedTB;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Elapsed = Stopwatch.StartNew();
    private static DateTimeOffset launchedAt;
    private static DateTimeOffset? logonAt;
    private static bool automatic;
    private static int workerLogged;
    private static string sessionError;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoundedTB", "startup.jsonl");

    public static void Start(bool isAutomatic)
    {
        launchedAt = DateTimeOffset.UtcNow;
        automatic = isAutomatic;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            // WTSSessionInfo reports this session's sign-in time, including local sessions.
            if (!WTSQuerySessionInformationW(IntPtr.Zero, -1, 24, out buffer, out var bytes))
                sessionError = "WTS query failed: " + Marshal.GetLastWin32Error();
            else if (bytes < Marshal.SizeOf<SessionInfo>())
                sessionError = "WTS returned an incomplete session record";
            else
            {
                var info = Marshal.PtrToStructure<SessionInfo>(buffer);
                if (info.LogonTime > 0)
                    logonAt = new DateTimeOffset(DateTime.FromFileTimeUtc(info.LogonTime));
                else sessionError = "Windows did not provide the session sign-in time";
            }
        }
        catch (Exception ex) { sessionError = ex.Message; }
        finally { if (buffer != IntPtr.Zero) WTSFreeMemory(buffer); }
        Mark("process-start");
    }

    public static void WorkerStarted()
    {
        if (Interlocked.Exchange(ref workerLogged, 1) == 0) Mark("taskbar-worker-start");
    }

    public static void Mark(string stage)
    {
        // Headless UI tests do not call Program.Main or collect session diagnostics.
        if (launchedAt == default) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var record = JsonSerializer.Serialize(new
            {
                timestampUtc = now, stage, automatic, processId = Environment.ProcessId,
                processStartedUtc = launchedAt, sessionLogonUtc = logonAt,
                secondsSinceSignIn = logonAt.HasValue ? (double?)(now - logonAt.Value).TotalSeconds : null,
                secondsFromSignInToLaunch = logonAt.HasValue ? (double?)(launchedAt - logonAt.Value).TotalSeconds : null,
                initializationSeconds = Elapsed.Elapsed.TotalSeconds,
                windowsUptimeSeconds = Environment.TickCount64 / 1000.0,
                sessionQueryError = sessionError
            });
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, record + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SessionInfo
    {
        public int State;
        public uint SessionId, IncomingBytes, OutgoingBytes, IncomingFrames, OutgoingFrames,
            IncomingCompressedBytes, OutgoingCompressedBytes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Station;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string Domain;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string User;
        public long ConnectTime, DisconnectTime, LastInputTime, LogonTime, CurrentTime;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int session,
        int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);
}
