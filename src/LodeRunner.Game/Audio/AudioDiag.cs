using System.Diagnostics;
using System.Text;

namespace LodeRunner.Game.Audio;

/// <summary>
/// Dev-only probe (enabled with <c>--audio-diag</c>) that records what the main thread does to the music stream:
/// time between feed calls, how many chunks each call had to render, synthesis cost and GC activity.
/// Writes <c>audio-diag.log</c> next to the executable. Does nothing unless enabled.
/// </summary>
internal static class AudioDiag
{
    /// <summary>Longest a stream can run dry: one sub-buffer at 44.1 kHz.</summary>
    private const double SubBufferMs = DarkAmbientSynth.BufferFrames * 1000.0 / 44100;

    private static readonly StringBuilder Log = new();
    private static readonly List<double> Gaps = [];
    private static long _start;
    private static long _lastUpdate;
    private static long _lastFlush;
    private static int _gc0, _gc1, _gc2;
    private static int _frames, _underruns, _lateFrames, _gcFrames;
    private static double _maxGap, _maxSynth;
    private static bool _held;

    public static bool Enabled { get; set; }

    /// <summary>Call at the start of every music update.</summary>
    public static void BeginUpdate(bool gameHeld)
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp();
        if (_start == 0)
        {
            _start = _lastUpdate = _lastFlush = now;
            _gc0 = GC.CollectionCount(0);
            _gc1 = GC.CollectionCount(1);
            _gc2 = GC.CollectionCount(2);
            Log.AppendLine($"# subBuffer={SubBufferMs:F1}ms  gcMode={System.Runtime.GCSettings.LatencyMode}  server={System.Runtime.GCSettings.IsServerGC}");
            return;
        }

        _held = gameHeld;
        double gap = Stopwatch.GetElapsedTime(_lastUpdate, now).TotalMilliseconds;
        _lastUpdate = now;
        _pendingGap = gap;
    }

    private static double _pendingGap;

    /// <summary>Call after the feed loop with the number of chunks rendered and the time spent synthesising.</summary>
    public static void EndUpdate(int chunks, double synthMs)
    {
        if (!Enabled || _start == 0 || _pendingGap == 0) return;

        double gap = _pendingGap;
        _pendingGap = 0;
        _frames++;
        Gaps.Add(gap);
        if (gap > _maxGap) _maxGap = gap;
        if (synthMs > _maxSynth) _maxSynth = synthMs;

        int g0 = GC.CollectionCount(0) - _gc0, g1 = GC.CollectionCount(1) - _gc1, g2 = GC.CollectionCount(2) - _gc2;
        _gc0 += g0; _gc1 += g1; _gc2 += g2;

        // Two or more chunks in one call means both sub-buffers had been consumed: the stream ran dry (audible glitch).
        bool underrun = chunks >= 2;
        bool late = gap > SubBufferMs * 0.75;
        bool gcHit = g0 + g1 + g2 > 0;
        if (underrun) _underruns++;
        if (late) _lateFrames++;
        if (gcHit) _gcFrames++;

        double t = Stopwatch.GetElapsedTime(_start).TotalSeconds;
        if (underrun || late || gcHit)
        {
            Log.AppendLine(
                $"t={t,8:F3}s gap={gap,6:F1}ms chunks={chunks} synth={synthMs,5:F2}ms gc={g0}/{g1}/{g2} held={_held}" +
                (underrun ? "  UNDERRUN" : "") + (late ? "  LATE" : ""));
        }

        if (Stopwatch.GetElapsedTime(_lastFlush).TotalSeconds >= 10) Flush();
    }

    public static void Flush()
    {
        if (!Enabled || _start == 0) return;
        _lastFlush = Stopwatch.GetTimestamp();
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "audio-diag.log"), Log + Summary());
        }
        catch (IOException)
        {
            // Diagnostics must never break the game.
        }
    }

    private static string Summary()
    {
        if (Gaps.Count == 0) return "";
        var sorted = Gaps.OrderBy(g => g).ToArray();
        double mean = sorted.Average();
        double p99 = sorted[(int)(sorted.Length * 0.99)];
        return $"""

            # SUMMARY frames={_frames} meanGap={mean:F1}ms p99Gap={p99:F1}ms maxGap={_maxGap:F1}ms maxSynth={_maxSynth:F2}ms
            #   underruns={_underruns} lateFrames(>75% of sub-buffer)={_lateFrames} framesWithGC={_gcFrames} totalGC={_gc0}/{_gc1}/{_gc2}

            """;
    }
}
