using System.Runtime.Versioning;
using FileAction.Core;

namespace FileAction.Desktop.Mac;

/// <summary>The files selected in Finder, asked through AppleScript (macOS asks once whether FileAction may control Finder).</summary>
[SupportedOSPlatform("macos")]
internal static class FinderSelection
{
    private static readonly string[] Script =
    [
        "tell application \"Finder\"",
        "set out to \"\"",
        "repeat with f in (get selection as alias list)",
        "set out to out & POSIX path of f & linefeed",
        "end repeat",
        "return out",
        "end tell",
    ];

    public static async Task<IReadOnlyList<string>> GetAsync(CancellationToken ct = default)
    {
        var args = Script.SelectMany(line => new[] { "-e", line });
        string output;
        try
        {
            output = await ProcessRunner.RunAsync("/usr/bin/osascript", args, ct);
        }
        catch (ConversionException)
        {
            return [];
        }
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Length > 1 ? p.TrimEnd('/') : p)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct()
            .ToList();
    }
}
