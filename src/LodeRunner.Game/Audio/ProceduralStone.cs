using Raylib_cs;

namespace LodeRunner.Game.Audio;

/// <summary>
/// Dark, heavy interface sounds: heavy stone slabs knocking together in a cavern. Each sound is one or more
/// impacts (sub thump + dull inharmonic stone modes + muffled grit) fed into a damped comb/all-pass echo.
/// Built in memory as stereo 16-bit PCM and loaded through Raylib, like <see cref="ProceduralFootsteps"/>.
/// </summary>
internal static class ProceduralStone
{
    private const int SampleRate = 44100;

    public enum UiKind
    {
        Move,
        Select,
        Back,
        Pause,
    }

    // One slab impact: start time, fundamental, loudness, decay rate (1/s), grit cutoff (Hz).
    private readonly record struct Hit(float Start, float Hz, float Gain, float Decay, float GritHz, float GritGain);

    public static byte[] Create(UiKind kind, int variant = 0)
    {
        // duration, comb feedback, comb damping, wet level, delay scale, hits
        (float duration, float feedback, float damp, float wet, float delayScale, Hit[] hits) = kind switch
        {
            // Light knock of one slab on another.
            UiKind.Move => (0.55f, 0.48f, 0.55f, 0.50f, 0.8f,
            new Hit[]
            {
                new Hit(0.000f, 150f, 0.80f, 46f, 1500f, 0.30f),
            }),
            // Confirmed: a heavy slab drops into place, with a short scrape just before.
            UiKind.Select => (0.95f, 0.62f, 0.60f, 0.62f, 1.0f,
            new Hit[]
            {
                new Hit(0.000f, 118f, 0.55f, 60f, 900f, 0.26f),
                new Hit(0.032f, 88f, 1.00f, 26f, 1150f, 0.34f),
            }),
            // Back: lower and more dragging, slab slides off and settles.
            UiKind.Back => (0.95f, 0.62f, 0.64f, 0.60f, 1.1f,
            new Hit[]
            {
                new Hit(0.000f, 84f, 0.95f, 24f, 800f, 0.36f),
                new Hit(0.085f, 64f, 0.55f, 30f, 650f, 0.22f),
            }),
            // Pause: two very heavy slabs, the second answers the first from further away.
            _ => (1.45f, 0.70f, 0.68f, 0.70f, 1.45f,
            new Hit[]
            {
                new Hit(0.000f, 66f, 1.00f, 15f, 700f, 0.40f),
                new Hit(0.150f, 54f, 0.70f, 17f, 560f, 0.28f),
            }),
        };

        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        var rng = new Random(5100 + (int)kind * 31 + variant * 101);
        var vr = ProceduralSfx.Rng(20 + (int)kind, variant);
        float pk = ProceduralSfx.J(vr, 0.05f);
        foreach (var h0 in hits)
        {
            var h = h0 with
            {
                Start = h0.Start * ProceduralSfx.J(vr, 0.12f),
                Hz = h0.Hz * pk * ProceduralSfx.J(vr, 0.02f),
                Gain = h0.Gain * ProceduralSfx.J(vr, 0.08f),
                Decay = h0.Decay * ProceduralSfx.J(vr, 0.10f),
            };
            AddHit(dry, h, rng);
        }

        // Stereo echo: slightly different delay lines per channel for width.
        ReadOnlySpan<int> baseL = [1559, 2203, 3019, 4093];
        ReadOnlySpan<int> baseR = [1693, 2377, 3251, 4421];
        var wetL = Comb(dry, baseL, delayScale, feedback, damp);
        var wetR = Comb(dry, baseR, delayScale, feedback, damp);
        ProceduralFootsteps.AllPassInPlace(wetL, 443, 0.5f);
        ProceduralFootsteps.AllPassInPlace(wetR, 491, 0.5f);

        var left = new float[frames];
        var right = new float[frames];
        float peak = 0.0001f;
        for (int i = 0; i < frames; i++)
        {
            left[i] = dry[i] + wetL[i] * wet;
            right[i] = dry[i] + wetR[i] * wet;
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
        }

        // Normalise, soft-clip, fade the end to zero.
        float norm = 0.85f / peak;
        int fade = (int)(SampleRate * 0.08f);
        for (int i = 0; i < frames; i++)
        {
            float f = i >= frames - fade ? (frames - 1 - i) / (float)fade : 1f;
            left[i] = MathF.Tanh(left[i] * norm * 1.1f) * f;
            right[i] = MathF.Tanh(right[i] * norm * 1.1f) * f;
        }

        return ProceduralFootsteps.ToWav(left, right);
    }

    private static void AddHit(float[] buf, Hit h, Random rng)
    {
        int start = (int)(h.Start * SampleRate);
        float phase = 0f;
        float gritLp = 0f, gritBp = 0f;
        float fGrit = Math.Clamp(2f * MathF.Sin(MathF.PI * h.GritHz / SampleRate), 0.01f, 0.45f);

        // Dull, inharmonic stone modes relative to the fundamental: (ratio, amplitude, extra decay factor).
        ReadOnlySpan<float> ratio = [1.00f, 1.47f, 2.31f, 3.62f];
        ReadOnlySpan<float> amp = [0.00f, 0.34f, 0.22f, 0.10f];
        ReadOnlySpan<float> dec = [1.0f, 1.5f, 2.0f, 3.0f];

        for (int i = 0; start + i < buf.Length; i++)
        {
            float t = i / (float)SampleRate;
            float attack = Math.Clamp(t / 0.002f, 0f, 1f);
            attack *= attack * (3f - 2f * attack);

            // Sub thump: pitch drops quickly onto the fundamental.
            float freq = h.Hz * (1f + 0.30f * MathF.Exp(-t * 45f));
            phase += freq / SampleRate;
            float tw = t * MathF.PI * 2f;
            float body = MathF.Sin(phase * MathF.PI * 2f) * MathF.Exp(-t * h.Decay);
            for (int m = 1; m < ratio.Length; m++)
                body += MathF.Sin(tw * h.Hz * ratio[m]) * amp[m] * MathF.Exp(-t * h.Decay * dec[m]);

            // Muffled contact grit: dull band-limited noise, very short.
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp = white - gritLp - 0.8f * gritBp;
            gritBp += fGrit * hp;
            gritLp += fGrit * gritBp;
            float grit = gritLp * MathF.Exp(-t * 120f) * h.GritGain;

            buf[start + i] += (body * 0.75f + grit) * attack * h.Gain;
        }
    }

    /// <summary>Parallel damped feedback combs: a dark stone-hall echo.</summary>
    internal static float[] Comb(float[] input, ReadOnlySpan<int> delays, float scale, float feedback, float damp)
    {
        var output = new float[input.Length];
        foreach (int d0 in delays)
        {
            int d = Math.Max(1, (int)(d0 * scale));
            var line = new float[d];
            int idx = 0;
            float lp = 0f;
            for (int i = 0; i < input.Length; i++)
            {
                float y = line[idx];
                lp = y * (1f - damp) + lp * damp;
                line[idx] = input[i] + lp * feedback;
                if (++idx >= d) idx = 0;
                output[i] += y * 0.35f;
            }
        }
        return output;
    }
}
