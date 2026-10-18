using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using FileAction.Core;

namespace FileAction.Desktop;

public sealed class App : Application
{
    public static IReadOnlyList<FileItem> StartupItems { get; set; } = [];

    /// <summary>Run as the menu-bar app with the drag gesture (macOS) instead of opening one wheel and quitting.</summary>
    public static bool AgentMode { get; set; }

    private IDisposable? _agent;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = DesktopSettings.Load();
            var controller = new Controller(settings, desktop, AgentMode);
            if (AgentMode && OperatingSystem.IsMacOS())
            {
                _agent = new Mac.Agent(settings, controller, () =>
                {
                    _agent?.Dispose();
                    desktop.Shutdown();
                });
            }
            else
            {
                var wheel = controller.CreateWheel(StartupItems);
                wheel.Show();
                wheel.Activate();
            }

            // Files opened with the app (Finder's "Open With", a file dropped on the Dock icon).
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs files)
                        controller.OpenFor(files.Files.Select(f => f.TryGetLocalPath()).OfType<string>().Select(p => new FileItem(p)).ToList());
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    public static AppBuilder BuildAvaloniaApp() =>
        WithBundledFont(AppBuilder.Configure<App>().UsePlatformDetect())
            .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true });

    /// <summary>
    /// Uses the bundled Inter font as the default, so the app doesn't depend on which fonts the system has
    /// (a minimal install may have none, and Avalonia then refuses to start).
    /// </summary>
    public static AppBuilder WithBundledFont(AppBuilder builder) =>
        builder.WithInterFont().With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });

    [STAThread]
    public static int Main(string[] args)
    {
        var settings = DesktopSettings.Load();
        Loc.Current = settings.Language ?? Loc.Detect();

        if (args.Length > 0 && Cli.Commands.Contains(args[0]))
            return Cli.Run(args);
        if (args.Length > 0 && args[0] == "--self-test")
            return SelfTest.Run(args.Length > 1 ? args[1] : "self-test");

        App.StartupItems = args
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal) && !a.StartsWith("-psn_", StringComparison.Ordinal))
            .Select(a => a.StartsWith("file://", StringComparison.Ordinal) ? new Uri(a).LocalPath : Path.GetFullPath(a))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p => new FileItem(p))
            .ToList();
        // On macOS the app lives in the menu bar; "fileaction" alone starts it (or brings up the drop zone with --drop-zone).
        App.AgentMode = OperatingSystem.IsMacOS() && (args.Contains("--agent") || (App.StartupItems.Count == 0 && !args.Contains("--drop-zone")));
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
