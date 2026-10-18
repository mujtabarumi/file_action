using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FileAction.Core;
using FileAction.Core.Operations;
using FileAction.Desktop.UI;
using static FileAction.Core.Loc;

namespace FileAction.Desktop;

/// <summary>Builds the wheels, runs operations with progress cards and quits when nothing is left on screen.</summary>
public sealed class Controller
{
    private readonly DesktopSettings _settings;
    private readonly ToolLocator _tools = new();
    private readonly IReadOnlyList<Operation> _conversions;
    private readonly IReadOnlyList<Operation> _actions;
    private readonly IClassicDesktopStyleApplicationLifetime? _lifetime;
    private readonly bool _agent;
    private JobsWindow? _jobs;
    private int _running;
    private int _wheels;
    private WheelWindow? _gesture;
    private DispatcherTimer? _gestureClose;

    /// <param name="agent">A long-lived menu-bar app: never quit when idle.</param>
    public Controller(DesktopSettings settings, IClassicDesktopStyleApplicationLifetime? lifetime, bool agent = false)
    {
        _settings = settings;
        _lifetime = lifetime;
        _agent = agent;
        _conversions = DesktopOperations.Conversions(settings);
        _actions = DesktopActions.Build(TrackAsync);
    }

    public IReadOnlyList<Operation> Operations(WheelKind wheel, IReadOnlyList<FileItem> items) =>
        WheelLayout.Arrange(Catalog.For(wheel == WheelKind.Actions ? _actions : _conversions, wheel, items, _tools),
            _settings.HiddenOperations, _settings.FavoriteOperations);

    public Operation? LastFor(IReadOnlyList<FileItem> items)
    {
        var op = _settings.LastOperationId is { } id ? _conversions.Concat(_actions).FirstOrDefault(o => o.Id == id) : null;
        return op is not null && op.AppliesTo(items) && op.IsAvailable(_tools, items) ? op : null;
    }

    /// <summary>A wheel for <paramref name="items"/>, or the drop zone when there are none.</summary>
    public WheelWindow CreateWheel(IReadOnlyList<FileItem> items) => Track(new WheelWindow(items, Operations, Execute) { LastOperation = LastFor });

    private WheelWindow Track(WheelWindow wheel)
    {
        _wheels++;
        wheel.Closed += (_, _) =>
        {
            _wheels--;
            ExitIfIdle();
        };
        return wheel;
    }

    private void Execute(Operation op, IReadOnlyList<FileItem> targets, bool repeat) => _ = ExecuteAsync(op, targets, repeat);

    /// <summary>Opens a wheel under the pointer for files chosen elsewhere (Finder selection, "Open With").</summary>
    public void OpenFor(IReadOnlyList<FileItem> items)
    {
        if (items.Count == 0)
            return;
        var wheel = CreateWheel(items);
        wheel.Show();
        wheel.Activate();
    }

    public void OpenDropZone()
    {
        var wheel = CreateWheel([]);
        wheel.Show();
        wheel.Activate();
    }

    // ------------------------------------------------------------------ the drag gesture (macOS)

    /// <summary>
    /// A wheel key was tapped. With a gesture wheel open, the same key toggles "repeat last" and another key
    /// switches wheels; otherwise a wheel opens under the pointer when something is being dragged.
    /// </summary>
    public void OnGesture(WheelKind wheel, bool dragging, (double X, double Y) cursor)
    {
        if (_gesture is { IsVisible: true } open)
        {
            if (wheel == open.CurrentWheel)
                open.ToggleRepeat();
            else
                open.SwitchTo(wheel);
            return;
        }
        if (!dragging)
            return;
        _gestureClose?.Stop();
        var window = Track(new WheelWindow(null, Operations, Execute, dragMode: true) { LastOperation = LastFor, AnchorPoints = cursor });
        window.SwitchTo(wheel);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_gesture, window))
                _gesture = null;
        };
        _gesture = window;
        window.Show();
    }

    /// <summary>The drag ended: a drop on the wheel closes it itself; a drop elsewhere leaves it, so close it shortly after.</summary>
    public void OnDragEnded()
    {
        if (_gesture is null)
            return;
        _gestureClose ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) =>
        {
            _gestureClose!.Stop();
            CloseGestureWheel();
        });
        _gestureClose.Start();
    }

    public void CloseGestureWheel()
    {
        _gesture?.Close();
        _gesture = null;
    }

    // ------------------------------------------------------------------ running operations

    private async Task ExecuteAsync(Operation op, IReadOnlyList<FileItem> items, bool repeat)
    {
        _running++;
        try
        {
            if (op is DesktopAction action)
            {
                Remember(op, null);
                try
                {
                    await action.RunAsync(items);
                }
                catch (Exception ex)
                {
                    ShowError(op.Label, ex.Message);
                }
                return;
            }

            IReadOnlyDictionary<string, string>? parameters = repeat ? _settings.LastParameters : null;
            if (op.Parameters.Count > 0 && parameters is not { Count: > 0 })
            {
                var dialog = new ParameterWindow(op, items);
                if (!await dialog.AskAsync())
                    return;
                parameters = dialog.Values;
            }
            Remember(op, parameters);

            var title = op.Wheel == WheelKind.Convert && op.Label.ToUpperInvariant() == op.Label ? T($"Převod na {op.Label}", $"Convert to {op.Label}") : op.Label;
            await TrackAsync(title, async ct =>
            {
                var context = new OperationContext
                {
                    Tools = _tools,
                    Parameters = parameters ?? new Dictionary<string, string>(),
                    OutputDirectory = string.IsNullOrWhiteSpace(_settings.OutputDirectory) ? null : _settings.OutputDirectory,
                    Settings = _settings.Quality,
                    CancellationToken = ct,
                    Progress = CurrentProgress,
                };
                var result = await Task.Run(() => op.ExecuteAsync(items, context), ct);
                _lastNote = result.Note;
                _lastWarning = result.Message;
                return result.Outputs;
            }, items.Count == 1 ? items[0].Name : Items(items.Count));
        }
        finally
        {
            _running--;
            ExitIfIdle();
        }
    }

    private IProgress<double> CurrentProgress = new Progress<double>();
    private string? _lastNote;
    private string? _lastWarning;

    /// <summary>Runs work with a progress card.</summary>
    public async Task TrackAsync(string title, Func<CancellationToken, Task<IReadOnlyList<string>>> work) =>
        await TrackAsync(title, work, "");

    private async Task TrackAsync(string title, Func<CancellationToken, Task<IReadOnlyList<string>>> work, string detail)
    {
        _jobs ??= CreateJobsWindow();
        using var cts = new CancellationTokenSource();
        var card = _jobs.Add(title, detail, () =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        _running++;
        try
        {
            CurrentProgress = new Progress<double>(card.Report);
            _lastNote = null;
            _lastWarning = null;
            var outputs = await work(cts.Token);
            card.Complete(outputs, _lastWarning, _lastNote);
        }
        catch (OperationCanceledException)
        {
            card.Cancelled();
        }
        catch (Exception ex)
        {
            card.Fail(ex is ConversionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _running--;
        }
    }

    private JobsWindow CreateJobsWindow()
    {
        var window = new JobsWindow();
        window.Emptied += ExitIfIdle;
        return window;
    }

    public void ShowError(string title, string message)
    {
        _jobs ??= CreateJobsWindow();
        _jobs.Add(title, "", () => { }).Fail(message);
    }

    /// <summary>A card that just says something (it goes away on its own).</summary>
    public void ShowNote(string title, string message)
    {
        _jobs ??= CreateJobsWindow();
        _jobs.Add(title, "", () => { }).Complete([], null, message);
    }

    private void Remember(Operation op, IReadOnlyDictionary<string, string>? parameters)
    {
        _settings.LastOperationId = op.Id;
        _settings.LastParameters = parameters is { Count: > 0 } ? new Dictionary<string, string>(parameters) : null;
        _settings.Save();
    }

    /// <summary>Quit once no wheel is open, nothing runs and no progress card is left (never for the menu-bar app).</summary>
    private void ExitIfIdle()
    {
        if (_agent || _wheels > 0 || _running > 0 || (_jobs is not null && _jobs.Cards.Children.Count > 0))
            return;
        _lifetime?.Shutdown();
    }
}
