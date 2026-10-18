using System.Globalization;
using System.Text.RegularExpressions;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>Audio/video work through the ffmpeg command line.</summary>
public static partial class FfmpegBackend
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimeRegex();

    /// <summary>Encoder arguments for a target container.</summary>
    public static IReadOnlyList<string> CodecArgs(string format, Settings s, bool hasVideo = true)
    {
        var crf = s.VideoCrf.ToString(CultureInfo.InvariantCulture);
        string[] h264 = ["-c:v", "libx264", "-preset", "medium", "-crf", crf, "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2"];
        return format switch
        {
            "mp4" => [.. h264, "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"],
            "mov" => [.. h264, "-c:a", "aac", "-b:a", "160k"],
            "mkv" => [.. h264, "-c:a", "aac", "-b:a", "160k"],
            "webm" => ["-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"],
            "gif" => ["-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4", "-loop", "0"],
            "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
            "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "aac" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
            "wav" => ["-vn", "-c:a", "pcm_s16le"],
            "aiff" => ["-vn", "-c:a", "pcm_s16be"],
            "flac" => ["-vn", "-c:a", "flac"],
            "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
            "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
            "wma" => ["-vn", "-c:a", "wmav2", "-b:a", "192k"],
            "avi" => ["-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "3"],
            "wmv" => ["-c:v", "wmv2", "-q:v", "4", "-c:a", "wmav2"],
            _ => [.. h264, "-c:a", "aac", "-b:a", "160k"],
        };
    }

    /// <summary>
    /// What to use instead of an encoder library a particular ffmpeg build lacks (Homebrew's may come without
    /// libvorbis), in order of preference. Opus in an Ogg file plays everywhere Vorbis does; ffmpeg's own vorbis
    /// encoder is experimental and only encodes stereo.
    /// </summary>
    private static readonly Dictionary<string, EncoderFallback[]> EncoderFallbacks = new()
    {
        ["libvorbis"] = [new(["libopus", "-b:a", "160k"], KeepsQuality: false), new(["vorbis", "-strict", "experimental", "-ac", "2"], KeepsQuality: true)],
        ["libopus"] = [new(["opus", "-strict", "experimental"], KeepsQuality: true)],
    };

    /// <param name="Args">What replaces the encoder name.</param>
    /// <param name="KeepsQuality">Whether the <c>-q:a</c> / <c>-b:a</c> that follows the encoder still applies.</param>
    private sealed record EncoderFallback(string[] Args, bool KeepsQuality);

    private static readonly Dictionary<string, Task<HashSet<string>>> Encoders = new();

    /// <summary>The names ffmpeg lists under <c>-encoders</c>, read once per ffmpeg binary.</summary>
    private static Task<HashSet<string>> EncodersAsync(string ffmpeg, CancellationToken ct)
    {
        lock (Encoders)
        {
            if (!Encoders.TryGetValue(ffmpeg, out var task))
            {
                Encoders[ffmpeg] = task = Task.Run(async () =>
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var output = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-encoders"], ct).ConfigureAwait(false);
                        foreach (var line in output.Split('\n'))
                        {
                            // " A....D libmp3lame           libmp3lame MP3 ..." – flags, name, description.
                            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && parts[0].Length == 6)
                                names.Add(parts[1]);
                        }
                    }
                    catch (ConversionException)
                    {
                    }
                    return names;
                }, ct);
            }
            return task;
        }
    }

    /// <summary>Replaces <c>-c:a libvorbis</c> and the like with ffmpeg's built-in encoder when the library isn't compiled in.</summary>
    private static async Task<List<string>> WithAvailableEncodersAsync(string ffmpeg, List<string> args, CancellationToken ct)
    {
        var wanted = args.Where((a, i) => i > 0 && args[i - 1].StartsWith("-c:", StringComparison.Ordinal) && EncoderFallbacks.ContainsKey(a)).ToList();
        if (wanted.Count == 0)
            return args;
        var available = await EncodersAsync(ffmpeg, ct).ConfigureAwait(false);
        if (available.Count == 0 || wanted.All(available.Contains))
            return args;
        var result = new List<string>(args.Count + 4);
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0 && args[i - 1].StartsWith("-c:", StringComparison.Ordinal) && EncoderFallbacks.TryGetValue(args[i], out var fallbacks) && !available.Contains(args[i]))
            {
                var fallback = fallbacks.FirstOrDefault(f => available.Contains(f.Args[0])) ?? fallbacks[^1];
                result.AddRange(fallback.Args);
                if (!fallback.KeepsQuality && i + 2 < args.Count && args[i + 1] is "-q:a" or "-b:a")
                    i += 2;
            }
            else
            {
                result.Add(args[i]);
            }
        }
        return result;
    }

    /// <summary>Runs ffmpeg with <c>-i input [args] output</c> and maps its time= output to progress.</summary>
    public static Task RunAsync(
        OperationContext ctx,
        IReadOnlyList<string> inputs,
        IEnumerable<string> args,
        string output,
        IProgress<double> progress,
        IEnumerable<string>? inputArgs = null,
        double? knownDuration = null)
    {
        var ffmpeg = ctx.Tools.Require(ExternalTool.Ffmpeg);
        var all = new List<string> { "-hide_banner", "-nostdin", "-y" };
        if (inputArgs is not null)
            all.AddRange(inputArgs);
        foreach (var i in inputs)
        {
            all.Add("-i");
            all.Add(i);
        }
        all.AddRange(args);
        all.Add(output);

        double total = knownDuration ?? 0;
        var noAudio = false;
        return RunWithHintsAsync();

        async Task RunWithHintsAsync()
        {
            try
            {
                await ProcessRunner.RunAsync(ffmpeg, await WithAvailableEncodersAsync(ffmpeg, all, ctx.CancellationToken).ConfigureAwait(false), ctx.CancellationToken, OnLine).ConfigureAwait(false);
            }
            catch (ConversionException) when (noAudio)
            {
                throw new ConversionException(T("Soubor neobsahuje zvukovou stopu.", "The file has no audio track."));
            }
        }

        void OnLine(string line)
        {
            if (line.Contains("does not contain any stream", StringComparison.Ordinal)
                || line.Contains("matches no streams", StringComparison.Ordinal))
                noAudio = true;
            if (knownDuration is null && DurationRegex().Match(line) is { Success: true } d)
                total += Seconds(d); // several inputs (concat) add up
            else if (total > 0 && TimeRegex().Match(line) is { Success: true } t)
                progress.Report(Math.Clamp(Seconds(t) / total, 0, 0.99));
        }
    }

    private static double Seconds(Match m) =>
        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
        + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

    public static Task<string> ConvertAsync(FileItem item, string format, OperationContext ctx, IProgress<double> progress) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
            RunAsync(ctx, [item.Path], CodecArgs(format, ctx.Settings), tmp, progress));

    /// <summary>Re-encodes in the source container with extra filter/encoder args; output gets a suffix.</summary>
    public static Task<string> TransformAsync(
        FileItem item,
        string suffix,
        OperationContext ctx,
        IProgress<double> progress,
        IEnumerable<string> args,
        string? outputFormat = null,
        IEnumerable<string>? inputArgs = null,
        double? duration = null)
    {
        var format = outputFormat ?? SafeContainer(item.Format);
        return OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({suffix})", format, tmp =>
            RunAsync(ctx, [item.Path], args, tmp, progress, inputArgs, duration));
    }

    /// <summary>Containers we can re-encode into; legacy ones become MP4.</summary>
    public static string SafeContainer(string format) => format switch
    {
        "avi" or "wmv" or "flv" or "mpg" or "mpeg" or "3gp" or "ts" or "mts" => "mp4",
        "wma" => "mp3",
        _ => format,
    };

    /// <summary>Parses "90", "1:30", "01:02:03.5" into seconds.</summary>
    public static double ParseTime(string text)
    {
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0)
            throw new ConversionException(T("Chybí čas.", "Missing time."));
        var parts = text.Split(':');
        double seconds = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
                throw new ConversionException(T($"Neplatný čas: {text}", $"Invalid time: {text}"));
            seconds = seconds * 60 + v;
        }
        return seconds;
    }

    public static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
