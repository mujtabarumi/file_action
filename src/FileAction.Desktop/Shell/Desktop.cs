using System.Diagnostics;
using FileAction.Core;

namespace FileAction.Desktop;

/// <summary>Opening files, folders and terminals through the desktop: <c>open</c> on macOS, xdg-open and the FileManager1 D-Bus interface on Linux.</summary>
public static class Desktop
{
    public static void Open(string path) => Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", path);

    /// <summary>Shows a file selected in the file manager, or opens its folder when that isn't supported.</summary>
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            Start("open", "-R", path);
            return;
        }
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        if (!Start("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", $"array:string:{uri}", "string:"))
            Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
    }

    /// <summary>A terminal in <paramref name="folder"/>: $TERMINAL or the first common one found (Terminal.app on macOS).</summary>
    public static bool Terminal(string folder)
    {
        var preferred = Environment.GetEnvironmentVariable("TERMINAL");
        if (OperatingSystem.IsMacOS())
        {
            // "open -a" accepts an app name (iTerm, Ghostty, …) or a path; Terminal opens a window in the folder.
            return (!string.IsNullOrEmpty(preferred) && Start("open", "-a", preferred, folder)) || Start("open", "-a", "Terminal", folder);
        }
        var candidates = new[] { preferred, "x-terminal-emulator", "kgx", "ptyxis", "gnome-terminal", "konsole",
            "xfce4-terminal", "alacritty", "kitty", "foot", "wezterm", "tilix", "xterm" };
        foreach (var name in candidates.Where(c => !string.IsNullOrEmpty(c)))
        {
            if (ToolLocator.Which(name!) is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = false });
                return true;
            }
        }
        return false;
    }

    /// <summary>Runs a program and waits up to 3 s for it; true when it exited successfully (or is still running).</summary>
    public static bool Start(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            if (!p.WaitForExit(3000))
                return true; // still running (e.g. xdg-open handing over): assume fine
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
