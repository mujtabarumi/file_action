using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;

namespace FileAction.Desktop.Mac;

/// <summary>The bits of CoreGraphics and CoreFoundation the app needs: the pointer position and display geometry.</summary>
[SupportedOSPlatform("macos")]
internal static partial class CoreGraphics
{
    public const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    [LibraryImport(Lib)]
    private static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(Lib)]
    private static partial CGPoint CGEventGetLocation(IntPtr e);

    [LibraryImport(Lib)]
    private static partial uint CGMainDisplayID();

    [LibraryImport(Lib)]
    private static partial CGRect CGDisplayBounds(uint display);

    /// <summary>The pointer in the global display space: points, origin at the top-left corner of the main display.</summary>
    public static CGPoint? PointerLocation()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero)
            return null;
        try
        {
            return CGEventGetLocation(e);
        }
        finally
        {
            CoreFoundation.CFRelease(e);
        }
    }

    /// <summary>
    /// CoreGraphics measures in points; Avalonia may report screens and window positions in device pixels.
    /// The ratio of the two for the main display (1 when Avalonia uses points too).
    /// </summary>
    public static double PixelsPerPoint(Screens screens)
    {
        var main = CGDisplayBounds(CGMainDisplayID());
        var primary = screens.Primary ?? screens.All.FirstOrDefault();
        if (primary is null || main.Size.Width <= 0)
            return 1;
        var ratio = primary.Bounds.Width / main.Size.Width;
        return Math.Abs(ratio - 1) < 0.01 ? 1 : ratio;
    }

    public static PixelPoint ToPixels(CGPoint p, Screens screens)
    {
        var scale = PixelsPerPoint(screens);
        return new PixelPoint((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale));
    }
}

[SupportedOSPlatform("macos")]
internal static partial class CoreFoundation
{
    public const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(Lib)]
    public static partial void CFRelease(IntPtr cf);

    /// <summary>The value of an exported global (e.g. a CFStringRef constant).</summary>
    public static IntPtr Constant(string library, string symbol) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(library), symbol));

    /// <summary>The address of an exported global (for structs passed by pointer, e.g. dictionary callbacks).</summary>
    public static IntPtr Address(string library, string symbol) => NativeLibrary.GetExport(NativeLibrary.Load(library), symbol);
}
