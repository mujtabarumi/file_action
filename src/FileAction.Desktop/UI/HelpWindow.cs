using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static FileAction.Core.Loc;

namespace FileAction.Desktop.UI;

/// <summary>"How it works" for the menu-bar app.</summary>
public sealed class HelpWindow : Window
{
    public HelpWindow(DesktopSettings settings)
    {
        Palette.Refresh();
        Title = T("FileAction – jak to funguje", "FileAction – how it works");
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Page;
        Foreground = Palette.Text;
        Icon = AppIcon.Window;

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 24), MaxWidth = 460, Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = T("Převod souborů bez klikání", "Click-free file conversion"), FontSize = 18, FontWeight = FontWeight.SemiBold });
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
        stack.Children.Add(Widgets.Button(T("Zavřít", "Close"), Close, primary: true));
        Content = stack;
    }
}
