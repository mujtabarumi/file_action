using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileAction.Desktop.Mac;

/// <summary>
/// Bringing the app to the front. A menu-bar app (LSUIElement) is never activated on its own, so a window it
/// shows opens behind whatever app the user is in; this activates it first.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class AppKit
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool RespondsTo(IntPtr receiver, IntPtr selector, IntPtr query);

    /// <summary>Activates the app (NSApp); must run on the main (UI) thread.</summary>
    public static void ActivateApp()
    {
        try
        {
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero)
                return;
            // macOS 14+: cooperative "activate" (works when the user just launched or reopened the app);
            // older systems: activateIgnoringOtherApps.
            var activate = sel_registerName("activate");
            if (RespondsTo(app, sel_registerName("respondsToSelector:"), activate))
                Send(app, activate);
            else
                SendBool(app, sel_registerName("activateIgnoringOtherApps:"), true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
