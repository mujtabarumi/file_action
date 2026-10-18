using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace FileAction.Desktop.Mac;

/// <summary>
/// A session-wide CoreGraphics event tap on its own thread that passes every event on unchanged. Detects "a modifier key was tapped
/// while the left button is dragging something" – the gesture that opens a wheel – plus the hotkey and Escape,
/// and forwards them to the UI thread. Creating the tap fails until the user grants the Accessibility permission.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class EventTap : IDisposable
{
    // CGEventType
    private const uint LeftMouseDown = 1;
    private const uint LeftMouseUp = 2;
    private const uint LeftMouseDragged = 6;
    private const uint KeyDown = 10;
    private const uint FlagsChanged = 12;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUserInput = 0xFFFFFFFF;

    // CGEventTapLocation / CGEventTapPlacement / CGEventTapOptions
    private const uint SessionEventTap = 1;
    private const uint HeadInsertEventTap = 0;
    private const uint DefaultTap = 0; // may modify events; covered by the Accessibility permission
    private const uint ListenOnly = 1; // read-only; needs the separate Input Monitoring permission

    // CGEventField
    private const uint KeyboardEventKeycode = 9;

    // CGEventFlags
    private const ulong FlagShift = 1UL << 17;
    private const ulong FlagControl = 1UL << 18;
    private const ulong FlagOption = 1UL << 19;
    private const ulong FlagCommand = 1UL << 20;

    // Virtual key codes (Carbon Events.h)
    private const long KeyEscape = 53;
    private const long KeySpace = 49;

    /// <summary>Movement (in points) before a press counts as a drag.</summary>
    private const double DragThreshold = 6;

    private delegate IntPtr Callback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo);

    private readonly Callback _callback; // kept alive: the native side holds only the function pointer
    private IntPtr _tap;
    private IntPtr _source;
    private IntPtr _runLoop;
    private Thread? _thread;

    private bool _leftDown;
    private CoreGraphics.CGPoint _downAt;
    private volatile bool _dragging;
    private ModifierKey _pendingTap;
    private double _cursorX;
    private double _cursorY;

    public EventTap()
    {
        _callback = OnEvent;
    }

    /// <summary>A modifier was pressed and released on its own while dragging.</summary>
    public event Action<ModifierKey>? ModifierTapped;

    /// <summary>The left button was released (any drag ended).</summary>
    public event Action? LeftButtonUp;

    public event Action? EscapePressed;

    /// <summary>Control + Option + Space.</summary>
    public event Action? HotkeyPressed;

    public bool IsDragging => _dragging;

    public bool IsRunning => _thread is not null;

    /// <summary>The last pointer position seen by the tap (points, global display space).</summary>
    public CoreGraphics.CGPoint Cursor => new() { X = Volatile.Read(ref _cursorX), Y = Volatile.Read(ref _cursorY) };

    /// <summary>Installs the tap; false when macOS refuses (the permission isn't granted yet).</summary>
    /// <summary>Events the tap has seen since it started (0 after a while means macOS isn't delivering any).</summary>
    public long EventCount => Interlocked.Read(ref _events);

    private long _events;

    /// <param name="active">
    /// An active tap (Accessibility permission) rather than listen-only (Input Monitoring). Both see the same
    /// events and this one passes every event on unchanged; macOS only differs in which permission it checks.
    /// </param>
    public bool Start(bool active = true)
    {
        if (_thread is not null)
            return true;
        using var ready = new ManualResetEventSlim();
        var ok = false;
        var thread = new Thread(() =>
        {
            var mask = (1UL << (int)LeftMouseDown) | (1UL << (int)LeftMouseUp) | (1UL << (int)LeftMouseDragged)
                | (1UL << (int)KeyDown) | (1UL << (int)FlagsChanged);
            _tap = CGEventTapCreate(SessionEventTap, HeadInsertEventTap, active ? DefaultTap : ListenOnly, mask, _callback, IntPtr.Zero);
            if (_tap == IntPtr.Zero)
            {
                ready.Set();
                return;
            }
            _source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
            _runLoop = CFRunLoopGetCurrent();
            CFRunLoopAddSource(_runLoop, _source, CoreFoundation.Constant(CoreFoundation.Lib, "kCFRunLoopCommonModes"));
            CGEventTapEnable(_tap, true);
            ok = true;
            ready.Set();
            CFRunLoopRun();
            CGEventTapEnable(_tap, false);
            CoreFoundation.CFRelease(_source);
            CoreFoundation.CFRelease(_tap);
            _source = _tap = _runLoop = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "FileAction event tap",
        };
        thread.Start();
        ready.Wait();
        if (ok)
            _thread = thread;
        return ok;
    }

    public void Dispose()
    {
        if (_thread is null)
            return;
        if (_runLoop != IntPtr.Zero)
            CFRunLoopStop(_runLoop);
        _thread.Join(1000);
        _thread = null;
    }

    private static readonly bool Trace = Environment.GetEnvironmentVariable("FILEACTION_DEBUG") is { Length: > 0 };

    private IntPtr OnEvent(IntPtr proxy, uint type, IntPtr e, IntPtr userInfo)
    {
        Interlocked.Increment(ref _events);
        try
        {
            if (Trace && type is KeyDown or FlagsChanged or LeftMouseDown or LeftMouseUp or LeftMouseDragged)
                Console.Error.WriteLine($"[tap] type={type} key={CGEventGetIntegerValueField(e, KeyboardEventKeycode)} flags={CGEventGetFlags(e):X} dragging={_dragging} pending={_pendingTap}");
            switch (type)
            {
                case TapDisabledByTimeout or TapDisabledByUserInput:
                    // macOS switches a slow tap off; ours never blocks, so just switch it back on.
                    CGEventTapEnable(_tap, true);
                    break;
                case LeftMouseDown:
                    _downAt = Track(e);
                    _leftDown = true;
                    _dragging = false;
                    break;
                case LeftMouseDragged:
                    var p = Track(e);
                    if (_leftDown && !_dragging && (Math.Abs(p.X - _downAt.X) > DragThreshold || Math.Abs(p.Y - _downAt.Y) > DragThreshold))
                        _dragging = true;
                    break;
                case LeftMouseUp:
                    Track(e);
                    _leftDown = false;
                    _dragging = false;
                    Post(() => LeftButtonUp?.Invoke());
                    break;
                case FlagsChanged:
                    OnFlags(CGEventGetIntegerValueField(e, KeyboardEventKeycode), CGEventGetFlags(e));
                    break;
                case KeyDown:
                    OnKeyDown(CGEventGetIntegerValueField(e, KeyboardEventKeycode), CGEventGetFlags(e));
                    break;
            }
        }
        catch
        {
            // Never let an exception escape into the system event chain.
        }
        return e;
    }

    private CoreGraphics.CGPoint Track(IntPtr e)
    {
        var p = CGEventGetLocation(e);
        Volatile.Write(ref _cursorX, p.X);
        Volatile.Write(ref _cursorY, p.Y);
        return p;
    }

    private static (ModifierKey Key, ulong Flag) Modifier(long keycode) => keycode switch
    {
        56 or 60 => (ModifierKey.Shift, FlagShift),
        59 or 62 => (ModifierKey.Control, FlagControl),
        58 or 61 => (ModifierKey.Option, FlagOption),
        54 or 55 => (ModifierKey.Command, FlagCommand),
        _ => (ModifierKey.None, 0),
    };

    private void OnFlags(long keycode, ulong flags)
    {
        var (key, flag) = Modifier(keycode);
        if (key == ModifierKey.None)
            return;
        var down = (flags & flag) != 0;
        if (down)
        {
            _pendingTap = _pendingTap == ModifierKey.None ? key : ModifierKey.None; // two modifiers together are a chord, not a tap
            return;
        }
        if (_pendingTap == key)
        {
            _pendingTap = ModifierKey.None;
            if (_dragging)
                Post(() => ModifierTapped?.Invoke(key));
        }
    }

    private void OnKeyDown(long keycode, ulong flags)
    {
        _pendingTap = ModifierKey.None; // a modifier + key combination is a shortcut, not a tap
        var mods = flags & (FlagShift | FlagControl | FlagOption | FlagCommand);
        if (keycode == KeyEscape && mods == 0)
            Post(() => EscapePressed?.Invoke());
        else if (keycode == KeySpace && mods == (FlagControl | FlagOption))
            Post(() => HotkeyPressed?.Invoke());
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    [LibraryImport(CoreGraphics.Lib)]
    private static partial IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, Callback callback, IntPtr userInfo);

    [LibraryImport(CoreGraphics.Lib)]
    private static partial void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(CoreGraphics.Lib)]
    private static partial CoreGraphics.CGPoint CGEventGetLocation(IntPtr e);

    [LibraryImport(CoreGraphics.Lib)]
    private static partial ulong CGEventGetFlags(IntPtr e);

    [LibraryImport(CoreGraphics.Lib)]
    private static partial long CGEventGetIntegerValueField(IntPtr e, uint field);

    [LibraryImport(CoreFoundation.Lib)]
    private static partial IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, long order);

    [LibraryImport(CoreFoundation.Lib)]
    private static partial IntPtr CFRunLoopGetCurrent();

    [LibraryImport(CoreFoundation.Lib)]
    private static partial void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [LibraryImport(CoreFoundation.Lib)]
    private static partial void CFRunLoopRun();

    [LibraryImport(CoreFoundation.Lib)]
    private static partial void CFRunLoopStop(IntPtr runLoop);
}
