using Avalonia;
using Avalonia.Controls;

namespace FileAction.Desktop.Shell;

/// <summary>Where the mouse pointer is, in the coordinates Avalonia positions windows in, to open the wheel under it.</summary>
public static class Pointer
{
    public static PixelPoint? Position(Window window)
    {
        if (OperatingSystem.IsMacOS())
            return Mac.CoreGraphics.PointerLocation() is { } p ? Mac.CoreGraphics.ToPixels(p, window.Screens) : null;
        if (OperatingSystem.IsLinux())
            return X11.Pointer() is { } p ? new PixelPoint(p.X, p.Y) : null;
        return null;
    }

    /// <summary>How many of Avalonia's window-position units make one logical (DIP) unit of <paramref name="window"/>.</summary>
    public static double Scale(Window window) =>
        OperatingSystem.IsMacOS() ? Mac.CoreGraphics.PixelsPerPoint(window.Screens) : window.RenderScaling;
}
