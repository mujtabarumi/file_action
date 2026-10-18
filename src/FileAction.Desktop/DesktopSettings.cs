using System.Text.Json;
using System.Text.Json.Serialization;
using FileAction.Core;

namespace FileAction.Desktop;

/// <summary>A key tapped while dragging to open a wheel (macOS only: Linux doesn't let apps watch other programs' input).</summary>
public enum ModifierKey
{
    None,
    Shift,
    Control,
    Option,
    Command,
}

/// <summary>
/// Preferences (same names as on Windows), stored as JSON in
/// <c>~/.config/fileaction/settings.json</c> on Linux and <c>~/Library/Application Support/FileAction/settings.json</c> on macOS.
/// </summary>
public sealed class DesktopSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>$HOME; unlike GetFolderPath it doesn't come back empty when the folder is missing.</summary>
    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Folder => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Application Support", "FileAction")
        : Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(Home, ".config"),
            "fileaction");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Null = follow the system language.</summary>
    public Language? Language { get; set; }

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    public Settings Quality { get; set; } = new();
    public List<string> HiddenOperations { get; set; } = [];
    public List<string> FavoriteOperations { get; set; } = [];
    public string? LastOperationId { get; set; }
    public Dictionary<string, string>? LastParameters { get; set; }

    /// <summary>Tesseract languages for OCR, e.g. "ces+eng"; empty = pick from what is installed.</summary>
    public string? OcrLanguages { get; set; }

    // ---- the "drag and tap a key" gesture and the menu bar (macOS) ----

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Control;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Option;

    /// <summary>Control + Option + Space opens the wheel for the files selected in Finder.</summary>
    public bool Hotkey { get; set; } = true;

    public bool Paused { get; set; }
    public bool FirstRunDone { get; set; }

    public WheelKind? WheelFor(ModifierKey key)
    {
        if (key == ModifierKey.None)
            return null;
        if (key == ConvertKey)
            return WheelKind.Convert;
        if (key == ToolsKey)
            return WheelKind.Tools;
        if (key == ActionsKey)
            return WheelKind.Actions;
        return null;
    }

    public ModifierKey KeyFor(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => ConvertKey,
        WheelKind.Tools => ToolsKey,
        _ => ActionsKey,
    };

    public static string KeyName(ModifierKey key) => key switch
    {
        ModifierKey.Shift => "⇧ Shift",
        ModifierKey.Control => "⌃ Control",
        ModifierKey.Option => "⌥ Option",
        ModifierKey.Command => "⌘ Command",
        _ => "—",
    };

    public static DesktopSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath), Json) ?? new DesktopSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new DesktopSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
