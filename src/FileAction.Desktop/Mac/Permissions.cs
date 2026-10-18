using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileAction.Desktop.Mac;

/// <summary>
/// Watching the keyboard and mouse of other apps needs the Accessibility permission
/// (System Settings → Privacy &amp; Security → Accessibility); Input Monitoring also satisfies a listen-only tap.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class Permissions
{
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [LibraryImport(AppServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(AppServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(CoreGraphics.Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGPreflightListenEventAccess();

    [LibraryImport(CoreGraphics.Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRequestListenEventAccess();

    [LibraryImport(CoreFoundation.Lib)]
    private static partial IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, long count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    public static bool Granted => AXIsProcessTrusted() || CGPreflightListenEventAccess();

    /// <summary>Shows the system's Accessibility prompt (macOS shows it once per app); true when already trusted.</summary>
    public static bool Request()
    {
        try
        {
            var key = CoreFoundation.Constant(AppServices, "kAXTrustedCheckOptionPrompt");
            var yes = CoreFoundation.Constant(CoreFoundation.Lib, "kCFBooleanTrue");
            var options = CFDictionaryCreate(IntPtr.Zero, [key], [yes], 1,
                CoreFoundation.Address(CoreFoundation.Lib, "kCFTypeDictionaryKeyCallBacks"),
                CoreFoundation.Address(CoreFoundation.Lib, "kCFTypeDictionaryValueCallBacks"));
            try
            {
                var trusted = AXIsProcessTrustedWithOptions(options);
                if (!trusted)
                    CGRequestListenEventAccess();
                return trusted;
            }
            finally
            {
                if (options != IntPtr.Zero)
                    CoreFoundation.CFRelease(options);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static void OpenSettings() =>
        Desktop.Start("open", "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
}
