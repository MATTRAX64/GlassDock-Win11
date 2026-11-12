using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GlassDock;

internal static class DockRecovery
{
    private const string Watch = "--watch-dock";
    private const string Retry = "--dock-retry";

    private static string? recoveryPath;
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IncludeFields = true };
    private sealed record ShellSnapshot(uint AppBarState, Native.RECT WorkArea, Dictionary<long, Native.RECT> Taskbars);

    internal static void CaptureShell(uint state, Native.RECT area, Dictionary<IntPtr, Native.RECT> taskbars)
    {
        if (recoveryPath is null) return;
        var snapshot = new ShellSnapshot(state, area, taskbars.ToDictionary(pair => pair.Key.ToInt64(), pair => pair.Value));
        try
        {
            File.WriteAllText(recoveryPath, JsonSerializer.Serialize(snapshot, SnapshotOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("GlassDock shell snapshot: {0}", ex.Message);
        }
    }

    internal static void ClearShellSnapshot()
    {
        if (recoveryPath is not null) DeleteSnapshot(recoveryPath);
    }

    private static void DeleteSnapshot(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.TraceWarning("GlassDock shell snapshot: {0}", ex.Message); }
    }

    private static void RestoreShell(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var snapshot = JsonSerializer.Deserialize<ShellSnapshot>(File.ReadAllText(path), SnapshotOptions);
            if (snapshot is null) return;
            var appBar = new Native.APPBARDATA
            {
                size = (uint)Marshal.SizeOf<Native.APPBARDATA>(),
                parameter = (IntPtr)snapshot.AppBarState
            };
            Native.SHAppBarMessage(10, ref appBar);
            foreach (var (value, rect) in snapshot.Taskbars)
            {
                var handle = new IntPtr(value);
                var name = new StringBuilder(64);
                Native.GetClassName(handle, name, name.Capacity);
                if (name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) continue;
                Native.EnableWindow(handle, true);
                Native.SetWindowPos(handle, IntPtr.Zero, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x14);
                Native.ShowWindow(handle, 5);
            }
            var area = snapshot.WorkArea;
            Native.SystemParametersInfo(47, 0, ref area, 2);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("GlassDock shell recovery: {0}", ex.Message);
        }
    }

    internal static bool IsWatcher(string[] args) => args.Length > 0 && args[0] == Watch;

    internal static EventWaitHandle? Start(string[] args)
    {
        using var current = Process.GetCurrentProcess();
        EventWaitHandle? cleanExit = null;
        try
        {
            var token = Guid.NewGuid();
            var name = ExitEventName(token);
            cleanExit = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            var start = NewProcess();
            start.ArgumentList.Add(Watch);
            start.ArgumentList.Add(current.Id.ToString());
            start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString());
            start.ArgumentList.Add(token.ToString("N"));
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var watcher = Process.Start(start);
            recoveryPath = SnapshotPath(token);
            return cleanExit;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            cleanExit?.Dispose();
            Trace.TraceWarning("GlassDock recovery: {0}", ex.Message);
            return null;
        }
    }

    internal static async Task WatchAsync(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], out var pid) || !long.TryParse(args[2], out var started) || !Guid.TryParseExact(args[3], "N", out var token)) return;
        try
        {
            using var cleanExit = EventWaitHandle.OpenExisting(ExitEventName(token));
            using var dock = Process.GetProcessById(pid);
            var startedAt = dock.StartTime.ToUniversalTime();
            if (startedAt.Ticks != started) return;
            await dock.WaitForExitAsync();
            var path = SnapshotPath(token);
            var intentional = cleanExit.WaitOne(0);
            if (!intentional) RestoreShell(path);
            DeleteSnapshot(path);
            if (intentional) return;

            var attempts = 0;
            var restart = NewProcess();
            foreach (var arg in args.Skip(4))
            {
                if (arg.StartsWith(Retry + "=", StringComparison.Ordinal))
                    int.TryParse(arg.AsSpan(Retry.Length + 1), out attempts);
                else if (arg != "--settings")
                    restart.ArgumentList.Add(arg);
            }
            if (DateTime.UtcNow - startedAt >= TimeSpan.FromMinutes(1)) attempts = 0;
            if (attempts is < 0 or >= 3) return;
            restart.ArgumentList.Add($"{Retry}={attempts + 1}");
            await Task.Delay(TimeSpan.FromSeconds(1 << attempts));
            using var replacement = Process.Start(restart);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("GlassDock recovery: {0}", ex.Message);
        }
    }

    private static string ExitEventName(Guid token) => $"Local\\GlassDock.Exit.{token:N}";
    private static string SnapshotPath(Guid token) => Path.Combine(Path.GetTempPath(), $"GlassDock-shell-{token:N}.json");

    private static ProcessStartInfo NewProcess() => new(Environment.ProcessPath!) { UseShellExecute = false };
}
