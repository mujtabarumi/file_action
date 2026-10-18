using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileAction.Desktop.Mac;

/// <summary>
/// One menu-bar app at a time. Two would each install an event tap and open two wheels per gesture. A second
/// launch (double-clicking the app while it runs, the login item) asks the running one to show itself and quits.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class SingleInstance
{
    /// <summary>SIGUSR1 on macOS: "show your window".</summary>
    public const int ShowSignal = 30;

    private static FileStream? _lock;

    private static string LockPath => Path.Combine(DesktopSettings.Folder, "agent.lock");
    private static string PidPath => Path.Combine(DesktopSettings.Folder, "agent.pid");

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    /// <summary>True when this process is now the only menu-bar app (the lock is held until the process exits).</summary>
    public static bool TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(DesktopSettings.Folder);
            // FileShare.None takes an exclusive flock(2) on Unix; it goes away with the process, even after a crash.
            _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.WriteAllText(PidPath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // can't tell; better one app too many than none
        }
    }

    /// <summary>Asks the running menu-bar app to show its window; false when it couldn't be reached.</summary>
    public static bool ShowRunning()
    {
        try
        {
            if (!int.TryParse(File.ReadAllText(PidPath).Trim(), out var pid) || pid == Environment.ProcessId)
                return false;
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Contains("fileaction", StringComparison.OrdinalIgnoreCase))
                return false;
            return kill(pid, ShowSignal) == 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
