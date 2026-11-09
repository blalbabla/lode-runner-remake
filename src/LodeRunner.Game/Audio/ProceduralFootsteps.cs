using System.Text;
using Raylib_cs;

namespace LodeRunner.Game.Audio;

/// <summary>
/// Generates soft, natural procedural footstep, ladder-rung and rope-grip sounds
/// with a warm low-frequency thump, muted sole scuff, and subtle stone cavern early-reflection reverb.
/// Built in-memory as stereo 16-bit PCM WAVs and loaded via <see cref="Raylib.LoadWaveFromMemory"/>.
/// </summary>
internal static class ProceduralFootsteps
{
    private const int SampleRate = 44100;

    public enum StepKind
    {
        RunLeft,
        RunRight,
        ClimbLeft,
        ClimbRight,
        RopeLeft,
        RopeRight,
        GuardLeft,
        GuardRight,
    }

    public static Sound Create(StepKind kind)
    {
        bool metal = kind is StepKind.ClimbLeft or StepKind.ClimbRight;
        float duration = metal ? 0.30f : 0.24f; // soft tap + quiet cavern echo tail (longer for the ringing rung)
        int frames = (int)(SampleRate * duration);
        var dryL = new float[frames];
        var dryR = new float[frames];
        var rng = new Random(1000 + (int)kind * 73);

        // Configure acoustic character per surface & foot (left/right alternation).
        // Frequencies in 120..360 Hz range cut through the sub-bass drone clearly on any speakers.
        var (startHz, endHz, thumpGain, scuffGain, scuffCutoffHz, decayRate) = kind switch
        {
            StepKind.RunLeft    => (235f, 88f, 0.72f, 0.28f, 920f, 56f),
            StepKind.RunRight   => (258f, 96f, 0.68f, 0.26f, 980f, 58f),
            // Metal rung: the low thump is only the grip of the hand/foot; the ring is added below.
            StepKind.ClimbLeft  => (300f, 120f, 0.50f, 0.16f, 900f, 62f),
            StepKind.ClimbRight => (325f, 130f, 0.48f, 0.16f, 940f, 64f),
            StepKind.RopeLeft   => (265f, 110f, 0.50f, 0.32f, 950f, 64f),
            StepKind.RopeRight  => (288f, 118f, 0.48f, 0.32f, 1020f, 66f),
            StepKind.GuardLeft  => (195f, 74f, 0.62f, 0.22f, 780f, 52f),
            _                   => (212f, 80f, 0.60f, 0.22f, 830f, 54f),
        };

        float phase = 0f;
        float lpNoise = 0f;
        float bpNoise = 0f;
        float prevWhite = 0f;
        float clickLp = 0f;
        float fCut = Math.Clamp(2f * MathF.Sin(MathF.PI * scuffCutoffHz / SampleRate), 0.01f, 0.45f);

        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;

            // 1. Soft heel/sole thump: fundamental + 2nd harmonic with rapid pitch drop and rounded 2.5ms attack.
            float attack = Math.Clamp(t / 0.0025f, 0f, 1f);
            float thumpEnv = attack * MathF.Exp(-t * decayRate);
            float freq = endHz + (startHz - endHz) * MathF.Exp(-t * 90f);
            phase += freq / SampleRate;
            float p2 = phase * MathF.PI * 2f;
            float thump = (MathF.Sin(p2) * 0.72f + MathF.Sin(p2 * 1.95f) * 0.28f) * thumpEnv * thumpGain;

            // 2. Muted sole/fabric scuff: band-limited noise burst.
            float scuffEnv = attack * MathF.Exp(-t * (decayRate * 1.2f));
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp = white - lpNoise - 0.55f * bpNoise;
            bpNoise += fCut * hp;
            lpNoise += fCut * bpNoise;
            float scuff = (lpNoise * 0.55f + bpNoise * 0.45f) * scuffEnv * scuffGain;

            // Metal rung: inharmonic bar-like partials ring and decay at different rates (a dull steel "tink"),
            // plus a very short bright contact click. Left/right rungs differ in pitch.
            float body = 0f;
            if (metal)
            {
                float f0 = kind == StepKind.ClimbLeft ? 205f : 228f;
                float tw = t * MathF.PI * 2f;
                float metalAttack = Math.Clamp(t / 0.0006f, 0f, 1f);
                // Low, dull steel: a heavy low ring with soft, fast-dying upper partials (no bright "tink").
                float ring =
                    MathF.Sin(tw * f0)          * 0.60f * MathF.Exp(-t * 17f) +
                    MathF.Sin(tw * f0 * 1.51f)  * 0.30f * MathF.Exp(-t * 26f) +
                    MathF.Sin(tw * f0 * 2.76f)  * 0.22f * MathF.Exp(-t * 38f) +
                    MathF.Sin(tw * f0 * 4.10f)  * 0.07f * MathF.Exp(-t * 70f);
                // Contact click, low-passed so it is a dull knock rather than a sharp tick.
                clickLp += 0.22f * ((white - prevWhite) - clickLp);
                float click = clickLp * MathF.Exp(-t * 300f) * 0.22f;
                body = (ring * 0.50f + click) * metalAttack;
            }
            prevWhite = white;

            float sig = MathF.Tanh((thump + scuff + body) * 1.15f);
            dryL[i] = sig;
            dryR[i] = sig;
        }

        // 3. Stone cavern early reflections + diffused room tail (multi-tap stereo + Schroeder all-pass).
        ReadOnlySpan<int> tapsL = [617, 1367, 2381, 3660, 5204]; // 14ms, 31ms, 54ms, 83ms, 118ms
        ReadOnlySpan<int> tapsR = [750, 1588, 2690, 4057, 5777]; // 17ms, 36ms, 61ms, 92ms, 131ms
        ReadOnlySpan<float> tapGains = [0.34f, 0.25f, 0.18f, 0.12f, 0.08f];

        var mixL = new float[frames];
        var mixR = new float[frames];
        float echoLpL = 0f, echoLpR = 0f;

        for (int i = 0; i < frames; i++)
        {
            float revL = 0f, revR = 0f;
            for (int k = 0; k < tapsL.Length; k++)
            {
                int il = i - tapsL[k];
                int ir = i - tapsR[k];
                if (il >= 0) revL += dryL[il] * tapGains[k];
                if (ir >= 0) revR += dryR[ir] * tapGains[k];
            }

            // Warm stone damping on the reflections.
            echoLpL += 0.18f * (revL - echoLpL);
            echoLpR += 0.18f * (revR - echoLpR);

            mixL[i] = dryL[i] + echoLpL;
            mixR[i] = dryR[i] + echoLpR;
        }

        // Pass through a short all-pass diffuser to smear the taps into a soft cavern air tail.
        AllPassInPlace(mixL, 347, 0.48f);
        AllPassInPlace(mixR, 389, 0.48f);

        // Smooth fade-out on the last 25 ms so the buffer ends at exact zero.
        int fadeSamples = (int)(SampleRate * 0.025f);
        for (int i = 0; i < fadeSamples; i++)
        {
            int idx = frames - 1 - i;
            float fade = i / (float)fadeSamples;
            mixL[idx] *= fade;
            mixR[idx] *= fade;
        }

        return ToSound(mixL, mixR);
    }

    internal static byte[] ToWav(float[] left, float[] right) => EncodeStereoWav16(left, right);

    internal static Sound ToSound(float[] left, float[] right) => LoadWav(ToWav(left, right));

    /// <summary>Loads WAV bytes as a Raylib sound. Must run on the main thread (the DSP that produced them need not).</summary>
    internal static Sound LoadWav(byte[] wav)
    {
        var wave = Raylib.LoadWaveFromMemory(".wav", wav);
        var sound = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        return sound;
    }

    /// <summary>
    /// Muffled, quiet "dull thud" for the dig: rounded sub thump, low filtered rumble,
    /// soft crumbling-brick grit and a dark cavern tail. Deliberately free of harsh highs.
    /// </summary>
    public static byte[] CreateDig(int variant = 0)
    {
        const float duration = 0.55f;
        int frames = (int)(SampleRate * duration);
        var dryL = new float[frames];
        var dryR = new float[frames];
        var rng = new Random(7771 + variant * 13);
        var vr = ProceduralSfx.Rng(7, variant);
        float pf = ProceduralSfx.J(vr, 0.08f);   // pitch of the blast
        float df = ProceduralSfx.J(vr, 0.12f);   // how fast it dies away

        float phase = 0f;
        float rumLp1 = 0f, rumLp2 = 0f;   // two-pole low-pass for the rumble body
        float gritLp = 0f, gritBp = 0f;   // band-limited crumbling grit
        float fRum = Math.Clamp(2f * MathF.Sin(MathF.PI * 210f / SampleRate), 0.01f, 0.45f);
        float fGrit = Math.Clamp(2f * MathF.Sin(MathF.PI * 780f / SampleRate), 0.01f, 0.45f);

        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float attack = Math.Clamp(t / 0.006f, 0f, 1f);
            attack *= attack * (3f - 2f * attack); // smoothstep: no click

            // 1. Rounded sub impact, pitch falls 120 -> 50 Hz.
            float freq = (50f + 70f * MathF.Exp(-t * 28f)) * pf;
            phase += freq / SampleRate;
            float sub = MathF.Sin(phase * MathF.PI * 2f) * attack * MathF.Exp(-t * 11f * df) * 0.85f;

            // 2. Low rumble body: dark noise through a 210 Hz low-pass.
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            rumLp1 += fRum * (white - rumLp1);
            rumLp2 += fRum * (rumLp1 - rumLp2);
            float rumble = rumLp2 * 5.5f * attack * MathF.Exp(-t * 8f) * 0.8f;

            // 3. Crumbling bricks: sparse grit pulses in a dull band, delayed slightly, slow decay.
            float gate = rng.NextDouble() < 0.045 ? 1f : 0.25f;
            float gw = ((float)(rng.NextDouble() * 2.0 - 1.0)) * gate;
            float hp = gw - gritLp - 0.7f * gritBp;
            gritBp += fGrit * hp;
            gritLp += fGrit * gritBp;
            float gritEnv = Math.Clamp((t - 0.025f) / 0.03f, 0f, 1f) * MathF.Exp(-t * 7f);
            float grit = gritLp * gritEnv * 0.55f;

            float sig = MathF.Tanh((sub + rumble + grit) * 1.1f) * 0.9f;
            dryL[i] = sig;
            dryR[i] = sig;
        }

        // Dark cavern reflections.
        ReadOnlySpan<int> tapsL = [1100, 2480, 4300, 6700, 9500];
        ReadOnlySpan<int> tapsR = [1380, 2900, 4900, 7300, 10300];
        ReadOnlySpan<float> tapGains = [0.38f, 0.30f, 0.22f, 0.15f, 0.10f];
        var mixL = new float[frames];
        var mixR = new float[frames];
        float lpL = 0f, lpR = 0f;
        for (int i = 0; i < frames; i++)
        {
            float revL = 0f, revR = 0f;
            for (int k = 0; k < tapsL.Length; k++)
            {
                int il = i - tapsL[k];
                int ir = i - tapsR[k];
                if (il >= 0) revL += dryL[il] * tapGains[k];
                if (ir >= 0) revR += dryR[ir] * tapGains[k];
            }
            lpL += 0.10f * (revL - lpL);
            lpR += 0.10f * (revR - lpR);
            mixL[i] = dryL[i] + lpL;
            mixR[i] = dryR[i] + lpR;
        }

        AllPassInPlace(mixL, 521, 0.5f);
        AllPassInPlace(mixR, 587, 0.5f);

        int fadeSamples = (int)(SampleRate * 0.06f);
        for (int i = 0; i < fadeSamples; i++)
        {
            int idx = frames - 1 - i;
            float fade = i / (float)fadeSamples;
            mixL[idx] *= fade;
            mixR[idx] *= fade;
        }

        return ToWav(mixL, mixR);
    }

    internal static void AllPassInPlace(float[] data, int delay, float gain)
    {
        var buf = new float[delay];
        int idx = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float delayed = buf[idx];
            float v = data[i] - gain * delayed;
            buf[idx] = v;
            if (++idx >= delay) idx = 0;
            data[i] = delayed + gain * v;
        }
    }

    private static byte[] EncodeStereoWav16(float[] left, float[] right)
    {
        int frames = left.Length;
        int dataBytes = frames * 4; // 2 channels * 2 bytes per sample
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);                 // PCM chunk size
        bw.Write((short)1);           // AudioFormat = 1 (PCM)
        bw.Write((short)2);           // NumChannels = 2 (Stereo)
        bw.Write(SampleRate);         // SampleRate
        bw.Write(SampleRate * 4);     // ByteRate
        bw.Write((short)4);           // BlockAlign
        bw.Write((short)16);          // BitsPerSample = 16
        bw.Write("data"u8);
        bw.Write(dataBytes);

        for (int i = 0; i < frames; i++)
        {
            short sL = (short)Math.Clamp((int)(left[i] * 32767f), short.MinValue, short.MaxValue);
            short sR = (short)Math.Clamp((int)(right[i] * 32767f), short.MinValue, short.MaxValue);
            bw.Write(sL);
            bw.Write(sR);
        }

        bw.Flush();
        return ms.ToArray();
    }
}
