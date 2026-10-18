using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static FileAction.Core.Loc;

namespace FileAction.Desktop.UI;

/// <summary>"How it works" for the menu-bar app.</summary>
public sealed class HelpWindow : Window
{
    private readonly TextBlock _access = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontWeight = FontWeight.SemiBold };
    private readonly Control? _openSettings;

    /// <param name="keyboardAccess">Whether the drag gesture can see the keyboard (null: don't show the line, e.g. in the self-test).</param>
    /// <param name="openSettings">Opens System Settings at the Accessibility list.</param>
    public HelpWindow(DesktopSettings settings, bool? keyboardAccess = null, Action? openSettings = null)
    {
        Palette.Refresh();
        Title = T("FileAction – jak to funguje", "FileAction – how it works");
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Page;
        Foreground = Palette.Text;
        Icon = AppIcon.Window;
        // macOS may refuse to bring a menu-bar app forward, so float above other apps until it is clicked once.
        Topmost = true;
        Deactivated += (_, _) => Topmost = false;

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 24), MaxWidth = 460, Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = T("Převod souborů bez klikání", "Click-free file conversion"), FontSize = 18, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = T("FileAction běží v řádku nabídek (ikona vpravo nahoře). Toto okno můžete zavřít.",
                "FileAction runs in the menu bar (the icon at the top right). You can close this window."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Palette.Muted,
        });
        if (keyboardAccess is not null)
        {
            stack.Children.Add(_access);
            if (openSettings is not null)
            {
                _openSettings = Widgets.Button(T("Otevřít Nastavení → Zpřístupnění", "Open Settings → Accessibility"), openSettings, primary: true);
                stack.Children.Add(_openSettings);
            }
            SetKeyboardAccess(keyboardAccess.Value);
        }
        foreach (var line in new[]
                 {
                     T("1. Chyťte soubor ve Finderu (nebo na ploše) a začněte ho táhnout.", "1. Grab a file in Finder (or on the desktop) and start dragging it."),
                     T($"2. Během tažení ťukněte na klávesu: {DesktopSettings.KeyName(settings.ConvertKey)} = Převést, {DesktopSettings.KeyName(settings.ToolsKey)} = Nástroje, {DesktopSettings.KeyName(settings.ActionsKey)} = Akce.",
                       $"2. While dragging, tap a key: {DesktopSettings.KeyName(settings.ConvertKey)} = Convert, {DesktopSettings.KeyName(settings.ToolsKey)} = Tools, {DesktopSettings.KeyName(settings.ActionsKey)} = Actions."),
                     T("3. Pusťte soubor na výseč. Výsledek se uloží vedle originálu; originál se nemění. Puštění doprostřed kola akci zruší.",
                       "3. Drop the file on a slice. The result is saved next to the original, which is never changed. Drop in the middle to cancel."),
                     T("Stejná klávesa podruhé zopakuje poslední akci; jiná klávesa přepne kolo. Esc kolo zavře.",
                       "The same key again repeats the last action; another key switches wheels. Esc closes the wheel."),
                     T("Bez myši: vyberte soubory ve Finderu a stiskněte ⌃ Control + ⌥ Option + mezerník.",
                       "No mouse: select files in Finder and press ⌃ Control + ⌥ Option + Space."),
                     T("Pravým tlačítkem ve Finderu: Rychlé akce → FileAction… (přidá položka nabídky „Přidat do Finderu“).",
                       "Right-click in Finder: Quick Actions → FileAction… (added by “Add to Finder” in the menu)."),
                     T($"Klávesy a další volby jsou v {DesktopSettings.FilePath}.", $"Keys and other options live in {DesktopSettings.FilePath}."),
                 })
        {
            stack.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        }
        var hint = new TextBlock
        {
            Text = T("Sledování klávesnice během tažení vyžaduje oprávnění Zpřístupnění (Nastavení systému → Soukromí a zabezpečení).",
                "Watching the keyboard while you drag needs the Accessibility permission (System Settings → Privacy & Security)."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = Palette.Muted,
            Margin = new Thickness(0, 6, 0, 0),
        };
        stack.Children.Add(hint);
        stack.Children.Add(Widgets.Button(T("Zavřít", "Close"), Close, primary: keyboardAccess is not false));
        Content = stack;
    }

    /// <summary>Shows whether the drag gesture works yet (it needs the Accessibility permission).</summary>
    public void SetKeyboardAccess(bool allowed)
    {
        _access.Text = allowed
            ? T("✓ Gesto tažení je zapnuté.", "✓ The drag gesture is on.")
            : T("Gesto tažení zatím nefunguje: povolte FileAction v Nastavení systému → Soukromí a zabezpečení → Zpřístupnění. Je-li tam už zapnutá, vypněte ji a znovu zapněte (každé nové sestavení aplikace macOS bere jako novou aplikaci).",
                "The drag gesture is off until you allow FileAction in System Settings → Privacy & Security → Accessibility. If it is already on there, switch it off and on again (macOS treats every new build of the app as a new app).");
        _access.Foreground = allowed ? Palette.Success : Palette.Error;
        if (_openSettings is not null)
            _openSettings.IsVisible = !allowed;
    }
}
