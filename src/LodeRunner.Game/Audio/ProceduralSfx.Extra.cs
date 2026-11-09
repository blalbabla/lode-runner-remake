namespace LodeRunner.Game.Audio;

internal static partial class ProceduralSfx
{
    // ---------------------------------------------------------------- ladders: metallic taps

    /// <summary>Three quick, ringing raps on a steel rung, climbing in pitch.</summary>
    public static byte[] CreateLadders(int variant = 0)
    {
        var vr = Rng(6, variant);
        const float duration = 0.6f;
        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        var rng = new Random(2024 + variant * 17);

        float root = J(vr, 0.07f);
        float gap = 0.085f * J(vr, 0.12f);
        (float Start, float Hz, float Gain)[] taps =
        [
            (0f, 1050f * root * J(vr, 0.02f), 0.85f * J(vr, 0.08f)),
            (gap * J(vr, 0.08f), 1260f * root * J(vr, 0.02f), 0.95f * J(vr, 0.08f)),
            (gap * 2f * J(vr, 0.06f), 1520f * root * J(vr, 0.02f), 1.00f * J(vr, 0.08f)),
        ];
        float decayScale = J(vr, 0.12f);
        // Steel tube partials: inharmonic, upper ones die quickly.
        ReadOnlySpan<float> ratio = [1.00f, 2.32f, 4.25f, 6.63f];
        ReadOnlySpan<float> amp = [0.60f, 0.42f, 0.24f, 0.12f];
        ReadOnlySpan<float> decay = [16f, 26f, 44f, 70f];

        foreach (var (start, hz, gain) in taps)
        {
            int s0 = (int)(start * SampleRate);
            float prev = 0f;
            for (int i = 0; s0 + i < frames; i++)
            {
                float t = i / (float)SampleRate;
                float tw = t * MathF.PI * 2f;
                float attack = Math.Clamp(t / 0.0004f, 0f, 1f);
                float v = 0f;
                for (int m = 0; m < ratio.Length; m++)
                    v += (MathF.Sin(tw * hz * ratio[m]) + 0.5f * MathF.Sin(tw * hz * ratio[m] * 1.006f)) * amp[m] * MathF.Exp(-t * decay[m] * decayScale);

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                v += (white - prev) * MathF.Exp(-t * 900f) * 0.4f; // metal-on-metal contact tick
                prev = white;

                dry[s0 + i] += v * attack * gain;
            }
        }

        return Finish(dry, wet: 0.30f, feedback: 0.40f, damp: 0.45f, level: 0.72f, delayScale: 0.55f, lowCut: 0.02f);
    }

    // ---------------------------------------------------------------- enemy_die: demon dragged to hell

    /// <summary>
    /// A guard is banished: a layered infernal death. A bottomless growl (three voices a tritone and an octave apart),
    /// a shrieking wail of damned souls spiralling down, a rising hiss of breath, and at the end the heavy slam of
    /// a gate closing on the underworld, all in a huge, dark hall.
    /// </summary>
    public static byte[] CreateEnemyDie(int variant = 0)
    {
        var vr = Rng(8, variant);
        float duration = 2.3f * J(vr, 0.05f);
        int frames = (int)(SampleRate * duration);
        float ps = J(vr, 0.07f);
        float slamAt = 0.95f * J(vr, 0.08f);

        float Envelope(float t)
        {
            float a = Math.Clamp(t / 0.03f, 0f, 1f);
            float sustain = t < 0.7f ? 1f : MathF.Exp(-(t - 0.7f) * 2.2f);
            float tail = Math.Clamp((duration - t) / 0.4f, 0f, 1f);
            return a * sustain * tail;
        }

        float Fall(float t) => (36f + 135f * MathF.Exp(-t * 1.4f)) * ps;
        float rumbleRate = 9.5f * J(vr, 0.15f);

        var main = RenderVoice(
            duration, 6661 + variant * 41,
            pitch: t => Fall(t) * (1f + 0.06f * MathF.Sin(t * MathF.PI * 2f * rumbleRate)),
            formants: t =>
            {
                float close = 1f - MathF.Exp(-t * 2.0f); // "aah" closing to "uuu": sinking away
                return (760f - 480f * close, 1120f - 560f * close, 2500f - 420f * close);
            },
            envelope: Envelope, openQuotient: 0.40f, breath: 0.55f, jitter: 0.09f, brightness: 0.85f, drive: 4.2f);

        var dark = RenderVoice(
            duration, 6662 + variant * 41,
            pitch: t => Fall(t) * 1.414f * (1f + 0.08f * MathF.Sin(t * MathF.PI * 2f * 5.7f + 1f)),
            formants: t =>
            {
                float close = 1f - MathF.Exp(-t * 1.6f);
                return (520f - 270f * close, 860f - 330f * close, 2200f);
            },
            envelope: Envelope, openQuotient: 0.33f, breath: 0.70f, jitter: 0.12f, brightness: 0.55f, drive: 4.6f);

        var abyss = RenderVoice(
            duration, 6663 + variant * 41,
            pitch: t => Fall(t) * 0.5f * (1f + 0.10f * MathF.Sin(t * MathF.PI * 2f * 3.3f)),
            formants: t => (430f, 700f, 1900f),
            envelope: Envelope, openQuotient: 0.30f, breath: 0.40f, jitter: 0.10f, brightness: 0.45f, drive: 3.4f);

        // Wail of the damned: a high, tearing scream spiralling downward, with a wide shaky vibrato and heavy distortion.
        float wailStart = 0.08f;
        var wail = RenderVoice(
            duration, 6664 + variant * 41,
            pitch: t => ((420f * MathF.Exp(-t * 1.15f) + 85f) * ps) * (1f + 0.10f * MathF.Sin(t * MathF.PI * 2f * 6.3f) + 0.03f * MathF.Sin(t * 61f)),
            formants: t =>
            {
                float close = 1f - MathF.Exp(-t * 1.5f);
                return (950f - 380f * close, 1550f - 650f * close, 2900f - 500f * close);
            },
            envelope: t =>
            {
                float rise = Math.Clamp((t - wailStart) / 0.22f, 0f, 1f);
                float fade = t < 1.05f ? 1f : MathF.Exp(-(t - 1.05f) * 3.2f);
                float tail = Math.Clamp((duration - t) / 0.4f, 0f, 1f);
                float flutter = 0.82f + 0.18f * MathF.Sin(t * MathF.PI * 2f * 17f);
                return rise * fade * tail * flutter;
            },
            openQuotient: 0.22f, breath: 0.75f, jitter: 0.20f, brightness: 1.0f, drive: 5.5f, bandwidth: 1.3f);

        // Sub boom dropping into the abyss, and the gate slam at the end.
        var low = new float[frames];
        float ph = 0f, slamPh = 0f, slamLp = 0f;
        var nr = new Random(8800 + variant * 7);
        int slamStart = (int)(slamAt * SampleRate);
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            ph += (26f + 70f * MathF.Exp(-t * 2.4f)) * ps / SampleRate;
            float boom = MathF.Sin(ph * MathF.PI * 2f) * MathF.Exp(-t * 1.6f) * Math.Clamp(t / 0.01f, 0f, 1f);

            float slam = 0f;
            if (i >= slamStart)
            {
                float st = (i - slamStart) / (float)SampleRate;
                slamPh += (30f + 90f * MathF.Exp(-st * 30f)) / SampleRate;
                slamLp += 0.05f * (((float)nr.NextDouble() * 2f - 1f) - slamLp);
                float env = MathF.Exp(-st * 4.2f) * Math.Clamp(st / 0.003f, 0f, 1f);
                slam = (MathF.Sin(slamPh * MathF.PI * 2f) * 1.1f + slamLp * 3.5f * MathF.Exp(-st * 18f)) * env;
            }

            low[i] = boom * 0.7f + slam;
        }

        // Rising breath of hell: noise that swells and then falls away.
        var hiss = new float[frames];
        var hr = new Random(8900 + variant * 7);
        float hLp = 0f;
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float n = (float)hr.NextDouble() * 2f - 1f;
            hLp += 0.30f * (n - hLp);
            float swell = MathF.Min(1f, t / 0.5f) * MathF.Exp(-MathF.Max(0f, t - 0.5f) * 1.6f);
            hiss[i] = (n - hLp) * swell;
        }

        Normalize(main, 1f);
        Normalize(dark, 0.6f);
        Normalize(abyss, 0.55f);
        Normalize(wail, 0.55f);
        Normalize(low, 0.9f);
        Normalize(hiss, 0.2f);

        var dry = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float growl = 0.75f + 0.25f * MathF.Sin(t * MathF.PI * 2f * 27f); // rough amplitude flutter
            dry[i] = (main[i] + dark[i] + abyss[i]) * growl + wail[i] + low[i] + hiss[i];
        }

        return Finish(dry, wet: 0.55f, feedback: 0.68f, damp: 0.58f, level: 0.55f, delayScale: 1.9f, lowCut: 0.004f);
    }

    // ---------------------------------------------------------------- win: joyful choir

    /// <summary>Short major-key choral fanfare: sung "ah" in harmony, ending on a held C major chord.</summary>
    public static byte[] CreateWin(int variant = 0)
    {
        var vr = Rng(9, variant);
        const float duration = 5.2f;
        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        float tempo = J(vr, 0.06f);
        float key = J(vr, 0.035f);

        // (start, length, MIDI notes). Key of C major: rising G-C-E-G, then A-B, resolving on C major.
        (float Start, float Len, int[] Notes)[] chords =
        [
            (0.00f, 0.25f, [67, 64, 60, 48]),
            (0.25f, 0.25f, [72, 67, 64, 48]),
            (0.50f, 0.25f, [76, 72, 67, 48]),
            (0.75f, 0.55f, [79, 76, 72, 60, 48]),
            (1.30f, 0.30f, [81, 77, 72, 53]),
            (1.60f, 0.30f, [83, 79, 74, 55]),
            (1.95f, 2.05f, [84, 79, 76, 67, 60, 48]),
        ];

        int seed = 100 + variant * 997;
        foreach (var (start, len, notes) in chords)
            foreach (int midi in notes)
                AddChoirNote(dry, start * tempo, len * tempo, 440f * MathF.Pow(2f, (midi - 69) / 12f) * key, seed += 7, start < 1.9f);

        return Finish(dry, wet: 0.50f, feedback: 0.64f, damp: 0.42f, level: 0.85f, delayScale: 1.6f, lowCut: 0.006f);
    }

    /// <summary>
    /// One sung "ah" note from a small ensemble. Every singer has its own pitch offset, entry time, vibrato (slow,
    /// delayed), pitch scoop into the note, breath and formant wobble, so the result is a crowd, not one synth.
    /// </summary>
    private static void AddChoirNote(float[] dry, float start, float len, float hz, int seed, bool staccato)
    {
        const float release = 0.35f;
        var r = new Random(seed);
        bool low = hz < 260f;
        int singers = low ? 4 : 6;
        float level = Math.Clamp(200f / hz, 0.3f, 1.5f) * (staccato ? 0.30f : 0.26f);
        // Male-ish vowel for low notes, brighter "ah" for high ones; first formant follows high pitches.
        float f1 = low ? 700f : Math.Clamp(hz * 1.04f, 780f, 980f);
        float f2 = low ? 1100f : 1250f;
        float f3 = low ? 2550f : 2900f;

        for (int c = 0; c < singers; c++)
        {
            float entry = (float)r.NextDouble() * 0.045f;
            float detune = ((float)r.NextDouble() * 2f - 1f) * 0.011f;
            float vibRate = 4.6f + (float)r.NextDouble() * 1.5f;
            float vibDepth = 0.005f + (float)r.NextDouble() * 0.006f;
            float vibDelay = 0.12f + (float)r.NextDouble() * 0.15f;
            float vibPhase = (float)r.NextDouble() * 6.28f;
            float wobRate = 0.7f + (float)r.NextDouble() * 1.3f;
            float fw = (float)r.NextDouble() * 6.28f;
            float shade = 0.9f + (float)r.NextDouble() * 0.2f;
            float total = len + release - entry;
            float pan = 1f; // mono dry, room provides width

            var voice = RenderVoice(
                total, seed * 13 + c * 31,
                pitch: t =>
                {
                    float scoop = -0.03f * MathF.Exp(-t * 16f);                     // slides up into the note
                    float vib = vibDepth * Math.Clamp((t - vibDelay) / 0.25f, 0f, 1f) * MathF.Sin(t * MathF.PI * 2f * vibRate + vibPhase);
                    return hz * (1f + detune + scoop + vib);
                },
                formants: t => (f1 * shade * (1f + 0.03f * MathF.Sin(t * wobRate * 6.28f + fw)), f2 * shade, f3),
                envelope: t =>
                {
                    float a = Math.Clamp(t / 0.07f, 0f, 1f);
                    float rl = Math.Clamp((total - t) / release, 0f, 1f);
                    float swell = 0.85f + 0.15f * MathF.Sin(t * wobRate * 5f + fw);
                    return a * a * (3f - 2f * a) * rl * rl * swell;
                },
                openQuotient: 0.48f + (float)r.NextDouble() * 0.12f,
                breath: 0.10f + (float)r.NextDouble() * 0.06f,
                jitter: 0.03f,
                brightness: 0.85f,
                drive: 0f,
                bandwidth: 1.7f);

            int s0 = (int)((start + entry) * SampleRate);
            for (int i = 0; i < voice.Length && s0 + i < dry.Length; i++)
                dry[s0 + i] += voice[i] * level * pan;
        }
    }
}
