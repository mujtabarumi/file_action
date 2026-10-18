using System.Runtime.Versioning;
using Avalonia.Threading;
using FileAction.Core;
using FileAction.Desktop.UI;
using static FileAction.Core.Loc;

namespace FileAction.Desktop.Mac;

/// <summary>
/// The long-lived menu-bar app: the "drag and tap a key" gesture, the Finder-selection hotkey and the menu.
/// Wheels opened from the command line or Finder run in short-lived processes of their own.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class Agent : IDisposable
{
    private readonly DesktopSettings _settings;
    private readonly Controller _controller;
    private readonly EventTap _tap = new();
    private readonly MenuBar _menu;
    private DispatcherTimer? _retry;
    private HelpWindow? _help;
    private readonly System.Runtime.InteropServices.PosixSignalRegistration _showSignal;

    /// <param name="show">Show the help window right away (the app was started by hand, not by the login item).</param>
    public Agent(DesktopSettings settings, Controller controller, bool show, Action quit)
    {
        _settings = settings;
        _controller = controller;
        _menu = new MenuBar(
            finderSelection: () => _ = OpenFinderSelectionAsync(),
            dropZone: controller.OpenDropZone,
            openSettings: OpenSettingsFile,
            integrate: Integrate,
            help: ShowHelp,
            setPaused: paused =>
            {
                _settings.Paused = paused;
                _settings.Save();
            },
            paused: settings.Paused,
            quit: quit);

        _tap.ModifierTapped += OnModifierTapped;
        _tap.LeftButtonUp += controller.OnDragEnded;
        _tap.EscapePressed += controller.CloseGestureWheel;
        _tap.HotkeyPressed += () =>
        {
            if (_settings.Hotkey && !_settings.Paused)
                _ = OpenFinderSelectionAsync();
        };

        // A second launch (double-click while running) asks this one to show itself.
        _showSignal = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            (System.Runtime.InteropServices.PosixSignal)SingleInstance.ShowSignal, context =>
            {
                context.Cancel = true;
                Dispatcher.UIThread.Post(ShowHelp);
            });

        Log($"started (pid {Environment.ProcessId}, {(show ? "by hand" : "login item")})");
        if (!_settings.FirstRunDone)
        {
            _settings.FirstRunDone = true;
            _settings.Save();
            foreach (var line in Shell.Integration.Apply(true))
                Log(line);
            show = true;
        }
        StartTap();
        if (show)
            ShowHelp();
    }

    /// <summary>Installs the event tap, asking for the Accessibility permission and retrying until it is granted.</summary>
    private void StartTap()
    {
        if (_tap.Start())
        {
            _menu.PermissionMissing = false;
            Log("event tap installed: the drag gesture works");
            return;
        }
        _menu.PermissionMissing = true;
        Log("event tap refused: waiting for the Accessibility permission");
        Permissions.Request();
        _retry ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            if (!_tap.Start())
                return;
            _retry!.Stop();
            _menu.PermissionMissing = false;
            Log("event tap installed after the permission was granted");
            _help?.SetKeyboardAccess(true);
        });
        _retry.Start();
    }

    private void OnModifierTapped(ModifierKey key)
    {
        if (_settings.Paused || _settings.WheelFor(key) is not { } wheel)
            return;
        _controller.OnGesture(wheel, _tap.IsDragging, (_tap.Cursor.X, _tap.Cursor.Y));
    }

    private async Task OpenFinderSelectionAsync()
    {
        var paths = await FinderSelection.GetAsync();
        if (paths.Count == 0)
        {
            _controller.ShowError(T("Nic není vybráno", "Nothing selected"),
                T("Vyberte soubory ve Finderu a stiskněte zkratku znovu.", "Select files in Finder, then press the shortcut again."));
            return;
        }
        _controller.OpenFor(paths.Select(p => new FileItem(p)).ToList());
    }

    private void OpenSettingsFile()
    {
        if (!File.Exists(DesktopSettings.FilePath))
            _settings.Save();
        Desktop.Open(DesktopSettings.FilePath);
    }

    private void Integrate()
    {
        var lines = Shell.Integration.Apply(true).ToList();
        _controller.ShowNote(T("Přidáno do Finderu", "Added to Finder"), string.Join(Environment.NewLine, lines));
    }

    public void ShowHelp()
    {
        if (_help is { IsVisible: true })
        {
            AppKit.ActivateApp();
            _help.Activate();
            return;
        }
        _help = new HelpWindow(_settings, _tap.IsRunning, Permissions.OpenSettings);
        _help.Closed += (_, _) => _help = null;
        AppKit.ActivateApp();
        _help.Show();
        _help.Activate();
    }

    /// <summary>~/Library/Logs/FileAction.log: what the menu-bar app did, for when it seems to do nothing.</summary>
    public static string LogPath => Path.Combine(DesktopSettings.Home, "Library", "Logs", "FileAction.log");

    private static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        if (Environment.GetEnvironmentVariable("FILEACTION_DEBUG") is { Length: > 0 })
            Console.Error.WriteLine("[agent] " + line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                File.Delete(LogPath);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _showSignal.Dispose();
        _retry?.Stop();
        _tap.Dispose();
        _menu.Dispose();
    }
}
