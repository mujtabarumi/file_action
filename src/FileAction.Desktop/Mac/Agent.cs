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

    public Agent(DesktopSettings settings, Controller controller, Action quit)
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

        if (!_settings.FirstRunDone)
        {
            _settings.FirstRunDone = true;
            _settings.Save();
            foreach (var _ in Shell.Integration.Apply(true))
            {
            }
            ShowHelp();
        }
        StartTap();
    }

    /// <summary>Installs the event tap, asking for the Accessibility permission and retrying until it is granted.</summary>
    private void StartTap()
    {
        if (_tap.Start())
        {
            _menu.PermissionMissing = false;
            Debug("event tap installed");
            return;
        }
        _menu.PermissionMissing = true;
        Debug("event tap refused: asking for the Accessibility permission");
        Permissions.Request();
        _retry ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            if (!_tap.Start())
                return;
            _retry!.Stop();
            _menu.PermissionMissing = false;
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

    private void ShowHelp()
    {
        if (_help is { IsVisible: true })
        {
            _help.Activate();
            return;
        }
        _help = new HelpWindow(_settings);
        _help.Closed += (_, _) => _help = null;
        _help.Show();
        _help.Activate();
    }

    private static void Debug(string message)
    {
        if (Environment.GetEnvironmentVariable("FILEACTION_DEBUG") is { Length: > 0 })
            Console.Error.WriteLine("[agent] " + message);
    }

    public void Dispose()
    {
        _retry?.Stop();
        _tap.Dispose();
        _menu.Dispose();
    }
}
