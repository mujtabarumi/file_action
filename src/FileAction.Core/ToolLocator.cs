using static FileAction.Core.Loc;

namespace FileAction.Core;

/// <summary>Finds ffmpeg, pandoc and LibreOffice next to the app, in PATH or in their default install folders.</summary>
public sealed class ToolLocator
{
    private readonly Dictionary<ExternalTool, string?> _cache = new();
    private readonly string _appDir;

    public ToolLocator(string? appDir = null)
    {
        _appDir = appDir ?? AppContext.BaseDirectory;
    }

    private static bool IsWindows => OperatingSystem.IsWindows();
    private static bool IsMacOS => OperatingSystem.IsMacOS();

    /// <summary>Where Homebrew and MacPorts put programs; apps started from Finder don't inherit the shell's PATH.</summary>
    public static readonly string[] MacToolDirs = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"];

    public string? Ffmpeg => Find(ExternalTool.Ffmpeg);
    public string? Pandoc => Find(ExternalTool.Pandoc);
    public string? LibreOffice => Find(ExternalTool.LibreOffice);

    public bool Has(ExternalTool tool) => Find(tool) is not null;

    public void Refresh()
    {
        lock (_cache)
            _cache.Clear();
    }

    public string Require(ExternalTool tool) =>
        Find(tool) ?? throw new ConversionException(T($"Chybí nástroj {DisplayName(tool)}. {InstallHint(tool)}", $"{DisplayName(tool)} is missing. {InstallHint(tool)}"));

    public static string DisplayName(ExternalTool tool) => tool switch
    {
        ExternalTool.Ffmpeg => "FFmpeg",
        ExternalTool.Pandoc => "Pandoc",
        ExternalTool.LibreOffice => "LibreOffice",
        _ => tool.ToString(),
    };

    public static string InstallHint(ExternalTool tool)
    {
        var command = (tool, IsWindows, IsMacOS) switch
        {
            (ExternalTool.Ffmpeg, true, _) => "winget install Gyan.FFmpeg",
            (ExternalTool.Pandoc, true, _) => "winget install JohnMacFarlane.Pandoc",
            (ExternalTool.LibreOffice, true, _) => "winget install TheDocumentFoundation.LibreOffice",
            (ExternalTool.Ffmpeg, _, true) => "brew install ffmpeg",
            (ExternalTool.Pandoc, _, true) => "brew install pandoc",
            (ExternalTool.LibreOffice, _, true) => "brew install --cask libreoffice",
            (ExternalTool.Ffmpeg, _, _) => "sudo pacman -S ffmpeg / sudo apt install ffmpeg",
            (ExternalTool.Pandoc, _, _) => "sudo pacman -S pandoc-cli / sudo apt install pandoc",
            (ExternalTool.LibreOffice, _, _) => "sudo pacman -S libreoffice-fresh / sudo apt install libreoffice",
            _ => "",
        };
        return command.Length == 0 ? "" : T("Nainstalujte ho příkazem: ", "Install it with: ") + command;
    }

    public string? Find(ExternalTool tool)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(tool, out var path))
                _cache[tool] = path = Locate(tool);
            return path;
        }
    }

    /// <summary>The full path of program <paramref name="name"/> on PATH (plus the Homebrew folders on macOS), or null.</summary>
    public static string? Which(string name) => FindIn(SearchDirs(), [name]);

    private static IEnumerable<string> SearchDirs()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (IsWindows)
        {
            // Pick up tools installed after the app started (winget updates the registry, not our environment).
            pathVar += Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        }
        var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim('"'));
        return IsMacOS ? dirs.Concat(MacToolDirs) : dirs;
    }

    private static string? FindIn(IEnumerable<string> dirs, IReadOnlyList<string> names)
    {
        foreach (var dir in dirs)
        {
            foreach (var n in names)
            {
                try
                {
                    var full = Path.Combine(dir, n);
                    if (File.Exists(full))
                        return full;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    private string? Locate(ExternalTool tool)
    {
        var exe = tool switch
        {
            ExternalTool.Ffmpeg => "ffmpeg",
            ExternalTool.Pandoc => "pandoc",
            _ => "soffice",
        };

        var candidates = new List<string>();
        var bundledName = IsWindows ? exe + ".exe" : exe;
        candidates.Add(Path.Combine(_appDir, "tools", bundledName));
        candidates.Add(Path.Combine(_appDir, bundledName));

        if (tool == ExternalTool.LibreOffice && IsWindows)
        {
            // soffice.com is the console launcher: it waits for the conversion to finish.
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.com"));
                candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.exe"));
            }
        }

        if (tool == ExternalTool.LibreOffice && IsMacOS)
        {
            candidates.Add("/Applications/LibreOffice.app/Contents/MacOS/soffice");
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                candidates.Add(Path.Combine(home, "Applications", "LibreOffice.app", "Contents", "MacOS", "soffice"));
        }

        if (tool == ExternalTool.Pandoc && IsWindows)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Pandoc", "pandoc.exe"));
        }

        if (tool == ExternalTool.Ffmpeg && IsWindows)
        {
            // winget installs Gyan.FFmpeg into a versioned package folder and adds a link here.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var names = IsWindows
            ? (tool == ExternalTool.LibreOffice ? new[] { "soffice.com", "soffice.exe" } : new[] { exe + ".exe" })
            : new[] { exe, tool == ExternalTool.LibreOffice ? "libreoffice" : exe };
        return FindIn(SearchDirs(), names);
    }
}
