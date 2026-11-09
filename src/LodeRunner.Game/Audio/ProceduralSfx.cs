namespace LodeRunner.Game.Audio;

/// <summary>
/// Procedural gold-pickup bell and the guard/runner "voice" sounds (fall cry, "oh!", death scream, funeral bells).
/// Voices use a glottal pulse source through three time-varying formant filters (a vowel), plus breath noise,
/// vibrato and a little room echo. Every generator takes a <c>variant</c> number: variant N always gives the same
/// sound, but different variants shift pitch, timing and decay slightly, so repeated events do not sound identical.
/// All generators return WAV bytes and are thread-safe, so variants can be rendered in parallel at startup.
/// </summary>
internal static partial class ProceduralSfx
{
    private const int SampleRate = 44100;

    /// <summary>Deterministic random stream for one (sound, variant) pair.</summary>
    internal static Random Rng(int salt, int variant) => new(salt * 7919 + variant * 104729 + 17);

    /// <summary>Random multiplier in [1 - amount, 1 + amount].</summary>
    internal static float J(Random r, float amount) => 1f + ((float)r.NextDouble() * 2f - 1f) * amount;

    // ---------------------------------------------------------------- gold: small bell "ding"

    public static byte[] CreateGold(int variant = 0)
    {
        var vr = Rng(1, variant);
        const float duration = 0.75f;
        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        var rng = new Random(321 + variant * 17);

        // Small metal bell: inharmonic partials, higher ones die much faster. Each partial is a slightly
        // detuned pair so the ring shimmers a little.
        float f0 = 2350f * J(vr, 0.07f);
        float decayScale = J(vr, 0.15f);
        float detune = 1f + 0.0045f * J(vr, 0.4f);
        float tickAmount = 0.35f * J(vr, 0.3f);
        ReadOnlySpan<float> ratio = [1.00f, 2.76f, 5.40f, 8.93f];
        ReadOnlySpan<float> amp = [0.60f, 0.38f, 0.20f, 0.09f];
        ReadOnlySpan<float> decay = [7.5f, 12f, 21f, 38f];

        float prevWhite = 0f;
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float tw = t * MathF.PI * 2f;
            float attack = Math.Clamp(t / 0.0007f, 0f, 1f);

            float v = 0f;
            for (int m = 0; m < ratio.Length; m++)
            {
                float f = f0 * ratio[m];
                float env = MathF.Exp(-t * decay[m] * decayScale);
                v += (MathF.Sin(tw * f) + MathF.Sin(tw * f * detune) * 0.6f) * amp[m] * env;
            }

            // Strike: a very short, bright tick.
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float tick = white - prevWhite;
            prevWhite = white;
            v += tick * MathF.Exp(-t * 700f) * tickAmount;

            dry[i] = v * attack;
        }

        return Finish(dry, wet: 0.30f, feedback: 0.35f, damp: 0.50f, level: 0.78f, delayScale: 0.5f, lowCut: 0.02f);
    }

    // ---------------------------------------------------------------- voices

    /// <summary>Quiet, long, frightened "aaaah" while falling; rises in pitch and starts to tremble.</summary>
    public static byte[] CreateFall(int variant = 0)
    {
        var vr = Rng(2, variant);
        float duration = 1.7f * J(vr, 0.08f);
        float ps = J(vr, 0.07f);
        float vibRate = 6.2f * J(vr, 0.12f);
        float level = 0.20f * J(vr, 0.10f);
        return Voice(
            duration,
            seed: 777 + variant * 41,
            pitch: t =>
            {
                float rise = 1f - MathF.Exp(-t * 3.2f);
                float baseHz = (118f + 62f * rise - 14f * Math.Clamp((t - 0.7f) / 1f, 0f, 1f)) * ps; // male: ~120..180 Hz
                float vibDepth = 0.010f + 0.035f * Math.Clamp(t / 1.2f, 0f, 1f);
                return baseHz * (1f + vibDepth * MathF.Sin(t * MathF.PI * 2f * vibRate));
            },
            formants: t =>
            {
                float open = 1f - MathF.Exp(-t * 6f); // "o" opens into "a"
                return (520f + 220f * open, 950f + 260f * open, 2450f + 150f * open);
            },
            envelope: t =>
            {
                float a = Math.Clamp(t / 0.22f, 0f, 1f);
                float swell = 0.75f + 0.25f * MathF.Sin(t * 2.1f);
                float tail = Math.Clamp((duration - t) / 0.45f, 0f, 1f);
                return a * a * swell * tail;
            },
            openQuotient: 0.62f,
            breath: 0.18f,
            jitter: 0.004f,
            level: level,
            brightness: 0.42f);
    }

    /// <summary>Short startled, choked "oh!": quick pitch jump, tight throat, falls away fast.</summary>
    public static byte[] CreateTrap(int variant = 0)
    {
        var vr = Rng(3, variant);
        const float duration = 0.5f;
        float ps = J(vr, 0.08f);
        float decayRate = 7f * J(vr, 0.15f);
        float level = 0.36f * J(vr, 0.12f);
        return Voice(
            duration,
            seed: 913 + variant * 41,
            pitch: t =>
            {
                float jump = 1f - MathF.Exp(-t * 40f); // small lift to ~140 Hz, then sag
                float fall = Math.Clamp((t - 0.05f) / 0.4f, 0f, 1f);
                return (102f + 38f * jump - 40f * fall * fall) * ps;
            },
            formants: t =>
            {
                float close = Math.Clamp(t / 0.28f, 0f, 1f); // "o" closes slightly towards "u"
                return (500f - 110f * close, 820f - 160f * close, 2300f);
            },
            envelope: t =>
            {
                float a = Math.Clamp(t / 0.035f, 0f, 1f); // softer onset: an exhale, not a shout
                a *= a;
                float tail = Math.Clamp((duration - t) / 0.15f, 0f, 1f);
                return a * MathF.Exp(-t * decayRate) * tail;
            },
            openQuotient: 0.55f, // lax, breathy phonation
            breath: 0.75f,       // lots of air: voice rides on an exhale
            jitter: 0.02f,
            level: level,
            brightness: 0.45f);  // dull and muffled
    }

    /// <summary>
    /// Scream of pain and horror: male voice that tears upward into a strained, cracking "AAAH", then
    /// breaks and collapses into a ragged gasp. Pressed phonation, heavy air noise, hard saturation.
    /// </summary>
    public static byte[] CreateDeath(int variant = 0)
    {
        var vr = Rng(4, variant);
        float duration = 1.25f * J(vr, 0.06f);
        float ps = J(vr, 0.06f);
        float crackAt = 0.30f * J(vr, 0.15f);
        float vibRate = 7.4f * J(vr, 0.10f);
        return Voice(
            duration,
            seed: 4242 + variant * 41,
            pitch: t =>
            {
                float rise = 1f - MathF.Exp(-t * 9f);                       // violent upward tear
                float baseHz = (150f + 190f * rise) * ps;                   // ~150 -> ~340 Hz
                float crack = t > crackAt && t < crackAt + 0.08f ? 1.22f : 1f; // voice break
                float collapse = Math.Clamp((t - 0.75f) / 0.5f, 0f, 1f);
                baseHz = (baseHz * crack) * (1f - 0.42f * collapse * collapse);
                float vib = 0.03f + 0.04f * Math.Clamp(t / 0.8f, 0f, 1f);
                return baseHz * (1f + vib * MathF.Sin(t * MathF.PI * 2f * vibRate));
            },
            formants: t =>
            {
                float open = 1f - MathF.Exp(-t * 14f);                      // wide open "aah"
                return (600f + 330f * open, 1100f + 280f * open, 2600f + 250f * open);
            },
            envelope: t =>
            {
                float a = Math.Clamp(t / 0.025f, 0f, 1f);
                float sustain = t < 0.8f ? 1f : MathF.Exp(-(t - 0.8f) * 5.5f);
                float tail = Math.Clamp((duration - t) / 0.25f, 0f, 1f);
                float tremor = 0.88f + 0.12f * MathF.Sin(t * MathF.PI * 2f * 13f);
                return a * sustain * tail * tremor;
            },
            openQuotient: 0.30f,   // pressed, screaming phonation
            breath: 0.55f,
            jitter: 0.05f,
            level: 0.55f,
            brightness: 0.9f,
            drive: 2.6f);
    }

    /// <summary>Slow, sad funeral toll: a few low bells struck far apart, each ringing for seconds in a big hall.</summary>
    public static byte[] CreateGameOver(int variant = 0)
    {
        var vr = Rng(5, variant);
        const float duration = 8.0f;
        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        var rng = new Random(999 + variant * 17);

        // Strike time (s), bell fundamental (Hz), loudness. The last toll sinks a little lower.
        float pitch = J(vr, 0.012f);
        (float Start, float Hz, float Gain)[] tolls =
        [
            (0.00f, 174.6f, 1.00f),
            (2.10f * J(vr, 0.04f), 174.6f, 0.85f * J(vr, 0.08f)),
            (4.30f * J(vr, 0.04f), 164.8f, 0.78f * J(vr, 0.08f)),
            (6.50f * J(vr, 0.03f), 146.8f, 0.62f * J(vr, 0.08f)),
        ];

        // Church-bell partials: hum tone (0.5), prime, minor third (the "sad" interval), fifth, octave...
        ReadOnlySpan<float> ratio = [0.50f, 1.00f, 1.19f, 1.50f, 2.00f, 2.51f, 3.20f];
        ReadOnlySpan<float> amp = [0.55f, 0.70f, 0.52f, 0.30f, 0.26f, 0.12f, 0.06f];
        ReadOnlySpan<float> decay = [0.45f, 0.62f, 0.80f, 1.05f, 1.40f, 2.1f, 3.2f];

        foreach (var (start, hz0, gain) in tolls)
        {
            float hz = hz0 * pitch * J(vr, 0.004f);
            int s0 = (int)(start * SampleRate);
            float lp = 0f;
            for (int i = 0; s0 + i < frames; i++)
            {
                float t = i / (float)SampleRate;
                if (t > 6f) break;
                float tw = t * MathF.PI * 2f;
                float attack = Math.Clamp(t / 0.004f, 0f, 1f);

                float v = 0f;
                for (int m = 0; m < ratio.Length; m++)
                {
                    float f = hz * ratio[m];
                    float env = MathF.Exp(-t * decay[m]);
                    // Slightly detuned twin gives the slow beating shimmer of a large bell.
                    v += (MathF.Sin(tw * f) + 0.7f * MathF.Sin(tw * f * 1.0032f + 0.6f)) * amp[m] * env;
                }

                // Clapper impact: a dull, low thud.
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.06f * (n - lp);
                v += lp * MathF.Exp(-t * 60f) * 2.2f;

                dry[s0 + i] += v * attack * gain;
            }
        }

        // Dark, large hall: long damped echoes.
        return Finish(dry, wet: 0.55f, feedback: 0.66f, damp: 0.62f, level: 0.8f, delayScale: 2.1f, lowCut: 0.004f);
    }

    private static byte[] Voice(
        float duration,
        int seed,
        Func<float, float> pitch,
        Func<float, (float F1, float F2, float F3)> formants,
        Func<float, float> envelope,
        float openQuotient,
        float breath,
        float jitter,
        float level,
        float brightness,
        float drive = 0f)
    {
        var dry = RenderVoice(duration, seed, pitch, formants, envelope, openQuotient, breath, jitter, brightness, drive);
        return Finish(dry, wet: 0.28f, feedback: 0.32f, damp: 0.55f, level: level, delayScale: 0.6f, lowCut: 0.012f);
    }

    /// <summary>Glottal source + three formant filters. Returns dry mono samples (not normalised).</summary>
    private static float[] RenderVoice(
        float duration,
        int seed,
        Func<float, float> pitch,
        Func<float, (float F1, float F2, float F3)> formants,
        Func<float, float> envelope,
        float openQuotient,
        float breath,
        float jitter,
        float brightness,
        float drive = 0f,
        float bandwidth = 1f)
    {
        int frames = (int)(SampleRate * duration);
        var dry = new float[frames];
        var rng = new Random(seed);

        float phase = 0f;
        float prevFlow = 0f;
        float lpBreath = 0f;
        float jit = 0f;
        float lpVoice = 0f;
        var b1 = new Biquad();
        var b2 = new Biquad();
        var b3 = new Biquad();

        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;

            // Slow random pitch wobble (jitter).
            jit += 0.02f * (((float)rng.NextDouble() * 2f - 1f) - jit);
            float f = pitch(t) * (1f + jitter * jit * 6f);
            phase += f / SampleRate;
            if (phase >= 1f) phase -= 1f;

            // Glottal flow: smooth rise over the open quotient, quicker closing return phase.
            float flow;
            if (phase < openQuotient)
                flow = 0.5f * (1f - MathF.Cos(MathF.PI * phase / openQuotient));
            else
            {
                float k = (phase - openQuotient) / (1f - openQuotient);
                flow = MathF.Max(0f, 1f - k * 4f);
            }

            // Radiated source is the derivative of the flow.
            float src = (flow - prevFlow) * 38f;
            prevFlow = flow;

            // Aspiration noise, softly low-passed.
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            lpBreath += 0.45f * (n - lpBreath);
            src += lpBreath * breath * 0.9f;

            if ((i & 7) == 0) // formants move slowly: retune the filters every 8 samples, not every sample
            {
                var (f1, f2, f3) = formants(t);
                b1.SetBandpass(f1, f1 / (95f * bandwidth));
                b2.SetBandpass(f2, f2 / (120f * bandwidth));
                b3.SetBandpass(f3, f3 / (180f * bandwidth));
            }
            float y = b1.Process(src) + b2.Process(src) * 0.62f * brightness + b3.Process(src) * 0.30f * brightness * brightness;
            // One-pole low-pass: lower brightness darkens the whole voice.
            lpVoice += (0.15f + 0.85f * brightness) * (y - lpVoice);
            y = lpVoice;
            if (drive > 0f) y = MathF.Tanh(y * drive) / MathF.Tanh(drive); // hard saturation: strained, screaming edge

            dry[i] = y * envelope(t);
        }

        return dry;
    }

    internal static void Normalize(float[] buf, float peakTarget)
    {
        float peak = 0.0001f;
        foreach (float v in buf) peak = MathF.Max(peak, MathF.Abs(v));
        float k = peakTarget / peak;
        for (int i = 0; i < buf.Length; i++) buf[i] *= k;
    }

    // ---------------------------------------------------------------- shared

    /// <summary>Adds a small stereo room echo, normalises to <paramref name="level"/> and returns WAV bytes.</summary>
    private static byte[] Finish(float[] dry, float wet, float feedback, float damp, float level, float delayScale, float lowCut)
    {
        int frames = dry.Length;
        ReadOnlySpan<int> baseL = [1559, 2203, 3019, 4093];
        ReadOnlySpan<int> baseR = [1693, 2377, 3251, 4421];
        var wetL = ProceduralStone.Comb(dry, baseL, delayScale, feedback, damp);
        var wetR = ProceduralStone.Comb(dry, baseR, delayScale, feedback, damp);

        var left = new float[frames];
        var right = new float[frames];
        float peak = 0.0001f;
        float hpL = 0f, hpR = 0f;
        for (int i = 0; i < frames; i++)
        {
            float l = dry[i] + wetL[i] * wet;
            float r = dry[i] + wetR[i] * wet;
            // Gentle DC / rumble removal.
            hpL += lowCut * (l - hpL);
            hpR += lowCut * (r - hpR);
            left[i] = l - hpL;
            right[i] = r - hpR;
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
        }

        float norm = level / peak;
        int fade = (int)(SampleRate * 0.03f);
        for (int i = 0; i < frames; i++)
        {
            float f = i >= frames - fade ? (frames - 1 - i) / (float)fade : 1f;
            left[i] *= norm * f;
            right[i] *= norm * f;
        }

        return ProceduralFootsteps.ToWav(left, right);
    }

    /// <summary>RBJ band-pass biquad (constant 0 dB peak gain).</summary>
    private sealed class Biquad
    {
        private float _b0, _b2, _a1, _a2;
        private float _x1, _x2, _y1, _y2;

        public void SetBandpass(float hz, float q)
        {
            hz = Math.Clamp(hz, 40f, SampleRate * 0.45f);
            float w0 = 2f * MathF.PI * hz / SampleRate;
            float alpha = MathF.Sin(w0) / (2f * MathF.Max(0.3f, q));
            float a0 = 1f + alpha;
            _b0 = alpha / a0;
            _b2 = -alpha / a0;
            _a1 = -2f * MathF.Cos(w0) / a0;
            _a2 = (1f - alpha) / a0;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
