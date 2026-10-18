using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using FileAction.Desktop.UI;
using static FileAction.Core.Loc;

namespace FileAction.Desktop.Mac;

/// <summary>The icon in the menu bar with the app menu (the macOS counterpart of the Windows tray icon).</summary>
[SupportedOSPlatform("macos")]
internal sealed class MenuBar : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _pause;
    private readonly NativeMenuItem _permission;
    private bool _paused;

    public MenuBar(Action finderSelection, Action dropZone, Action openSettings, Action integrate, Action help,
        Action<bool> setPaused, bool paused, Action quit)
    {
        _paused = paused;
        var menu = new NativeMenu();
        _permission = Item(T("Povolit sledování klávesnice (Zpřístupnění)…", "Allow watching the keyboard (Accessibility)…"), Permissions.OpenSettings);
        _permission.IsVisible = false;
        menu.Add(_permission);
        menu.Add(Item(T("Kolo pro výběr ve Finderu", "Wheel for the Finder selection") + "\t⌃⌥␣", finderSelection));
        menu.Add(Item(T("Otevřít odkládací kolo", "Open the drop zone"), dropZone));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(T("Nastavení (settings.json)…", "Settings (settings.json)…"), openSettings));
        menu.Add(Item(T("Přidat do Finderu a spouštět po přihlášení", "Add to Finder and start at login"), integrate));
        menu.Add(Item(T("Jak to funguje", "How it works"), help));
        _pause = new NativeMenuItem(T("Pozastavit gesta", "Pause gestures")) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = paused };
        _pause.Click += (_, _) =>
        {
            _paused = !_paused;
            _pause.IsChecked = _paused;
            setPaused(_paused);
        };
        menu.Add(_pause);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(T("Ukončit FileAction", "Quit FileAction"), quit));

        var icon = AppIcon.Window;
        if (Environment.GetEnvironmentVariable("FILEACTION_DEBUG") is { Length: > 0 })
            Console.Error.WriteLine("[menu] icon " + (icon is null ? "missing" : "loaded"));
        _icon = new TrayIcon { Icon = icon, ToolTipText = "FileAction", Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(Application.Current!, [_icon]);
    }

    /// <summary>Shows or hides the "allow …" entry at the top of the menu.</summary>
    public bool PermissionMissing
    {
        set => _permission.IsVisible = value;
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    public void Dispose() => _icon.Dispose();
}
