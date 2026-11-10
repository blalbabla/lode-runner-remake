using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Audio;

/// <summary>
/// Real-time procedural Dark Cavern Ambient / Drone synthesizer.
/// Generates a continuous, never-repeating stereo stream with 5 layers:
/// 1. Sub-bass beating drone (fundamental + detuned sub-oscillator)
/// 2. Dark Phrygian/minor unison pad through a slowly breathing resonant State Variable Filter
/// 3. Subterranean cavern wind (filtered pink/brown noise with stereo drift)
/// 4. Rare distant metallic FM bell/chime echoes
/// 5. Stereo cross-feedback cavern delay + Schroeder all-pass diffusion reverb
/// Reacts smoothly to enemy proximity (tension), exit reveal (shimmer), and menu state (muffle).
/// </summary>
internal sealed class DarkAmbientSynth : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    // One sub-buffer (the stream has two). The main thread refills a sub-buffer once per rendered frame, so the
    // sub-buffer length is the longest frame hitch that can pass without an audible dropout: 4096 frames ~ 93 ms.
    // Latency does not matter for ambient music (sound effects use their own sounds).
    internal const int BufferFrames = 4096;

    // Dark cavern root frequencies in Hz — one octave up from C1 range so the pad and bells are audible
    // on full-range speakers as well as on laptop drivers. (D2, C2, Eb2, Bb1, F2, C#2)
    private static readonly float[] RootNotes =
    [
        73.416f, // D2
        65.406f, // C2
        77.782f, // Eb2
        58.270f, // Bb1
        87.308f, // F2
        69.296f, // C#2
    ];

    private readonly AudioStream _stream;
    private readonly bool _valid;
    private bool _started;
    private readonly float[] _buffer = new float[BufferFrames * Channels];
    private readonly Random _rng = new(1337);

    // Stereo ping-pong delay lines (~470 ms L, ~630 ms R) + all-pass diffusion buffers.
    private readonly float[] _delayL = new float[20727];
    private readonly float[] _delayR = new float[27783];
    private readonly float[] _ap1L = new float[1597];
    private readonly float[] _ap1R = new float[1777];
    private readonly float[] _ap2L = new float[557];
    private readonly float[] _ap2R = new float[619];
    private int _dlIdx, _drIdx, _ap1LIdx, _ap1RIdx, _ap2LIdx, _ap2RIdx;
    private float _delayLpL, _delayLpR;

    // Oscillator phases (0..1).
    private float _subPhase1, _subPhase2;
    private readonly float[] _padPhases = new float[9]; // 3 chord notes x 3 unison voices
    private float _tensionPhase1, _tensionPhase2;
    private float _hopePhase1, _hopePhase2;
    private float _lfoPhase1, _lfoPhase2, _lfoPhase3;

    // Harmonic progression state.
    private float _curRoot = RootNotes[0];
    private float _targetRoot = RootNotes[0];
    private float _noteTimer = 28f;
    private int _lastLevel = -1;

    // Resonant 2-pole SVF states (stereo pad + stereo wind).
    private float _padLpL, _padBpL, _padLpR, _padBpR;
    private float _windLpL, _windBpL, _windLpR, _windBpR;
    private float _brownL, _brownR;

    // Distant FM bell / cavern chime state.
    private float _bellPhaseC, _bellPhaseM;
    private float _bellFreq;
    private float _bellEnv;
    private float _bellPan = 0.5f;
    private float _bellTimer = 9f;

    // Smooth gameplay-reactive parameters (0..1).
    private float _tension;
    private float _targetTension;
    private float _exitOpen;
    private float _targetExitOpen;
    private float _muffle = 1f;
    private float _targetMuffle = 1f;
    private float _masterEnv; // 3-second fade-in on startup so filters settle without any initial thump

    private float _volume = 0.5f;

    public DarkAmbientSynth(bool audioReady)
    {
        if (!audioReady) return;

        Raylib.SetAudioStreamBufferSizeDefault(BufferFrames);
        _stream = Raylib.LoadAudioStream(SampleRate, 32, Channels);
        _valid = Raylib.IsAudioStreamValid(_stream);
        if (!_valid) return;

        for (int i = 0; i < _padPhases.Length; i++)
            _padPhases[i] = (float)_rng.NextDouble();

        Raylib.SetAudioStreamVolume(_stream, _volume);
        // Playback starts from Update() only after the first buffers are filled; a stream that plays while nobody
        // feeds it (e.g. during loading) loops its last buffer and is heard as a buzz/click.
    }

    /// <summary>Music volume in [0, 1] (scaled alongside Raylib's master volume).</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_valid) Raylib.SetAudioStreamVolume(_stream, _volume);
        }
    }

    public void Update(GameWorld world, bool gameHeld, int levelIndex)
    {
        if (!_valid) return;

        if (_volume <= 0.001f)
        {
            if (Raylib.IsAudioStreamPlaying(_stream))
                Raylib.PauseAudioStream(_stream);
            return;
        }

        // Shift root note when entering a new level.
        if (levelIndex != _lastLevel)
        {
            _lastLevel = levelIndex;
            _targetRoot = RootNotes[Math.Abs(levelIndex) % RootNotes.Length];
        }

        // Compute gameplay reactivity targets.
        _targetMuffle = gameHeld ? 1f : 0f;
        _targetExitOpen = !gameHeld && world.Phase == GamePhase.Playing && world.GoldRemaining == 0 ? 1f : 0f;

        float nearestDist = 99f;
        if (!gameHeld && world.Phase == GamePhase.Playing)
        {
            float px = world.Player.X / (float)world.Grid.TileUnits;
            float py = world.Player.Y / (float)world.Grid.TileUnits;
            foreach (var e in world.Enemies)
            {
                if (!e.IsAlive || e.State != EnemyState.Active) continue;
                float dx = e.X / (float)world.Grid.TileUnits - px;
                float dy = e.Y / (float)world.Grid.TileUnits - py;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < nearestDist) nearestDist = d;
            }
        }
        // Tension rises when an active guard is within 7 tiles of the player.
        _targetTension = Math.Clamp(1f - (nearestDist - 1.5f) / 6.0f, 0f, 1f);

        AudioDiag.BeginUpdate(gameHeld);
        int chunks = 0;
        double synthMs = 0;
        while (Raylib.IsAudioStreamProcessed(_stream))
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            SynthesizeChunk(_buffer);
            synthMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            Raylib.UpdateAudioStream(_stream, (ReadOnlySpan<float>)_buffer, BufferFrames);
            chunks++;
        }
        // Start/resume only now, after the buffers above are filled.
        if (!_started)
        {
            Raylib.PlayAudioStream(_stream);
            _started = true;
        }
        else if (!Raylib.IsAudioStreamPlaying(_stream))
        {
            Raylib.ResumeAudioStream(_stream);
        }
        AudioDiag.EndUpdate(chunks, synthMs);
    }

    private void SynthesizeChunk(float[] buf)
    {
        const float dt = 1f / SampleRate;
        const float twoPi = MathF.PI * 2f;

        // Detune factors for 3-voice unison per chord note.
        ReadOnlySpan<float> detune = [0.9945f, 1.0000f, 1.0058f];
        // Dark chord intervals above root: Root (2x), Perfect Fifth (3x), Minor Sixth / Phrygian inflection (3.2x).
        ReadOnlySpan<float> chordMul = [2.0f, 3.0f, 3.1748f];

        for (int i = 0; i < BufferFrames; i++)
        {
            // Smooth parameter envelopes per sample to prevent zipper noise.
            _tension += (_targetTension - _tension) * 0.00015f;
            _exitOpen += (_targetExitOpen - _exitOpen) * 0.00010f;
            _muffle += (_targetMuffle - _muffle) * 0.00015f;
            _curRoot += (_targetRoot - _curRoot) * 0.000025f;

            // Autonomous slow root note drift every ~25..45 seconds.
            _noteTimer -= dt;
            if (_noteTimer <= 0f)
            {
                _noteTimer = 24f + (float)_rng.NextDouble() * 22f;
                _targetRoot = RootNotes[_rng.Next(RootNotes.Length)];
            }

            // Slow non-commensurate LFOs for organic movement.
            _lfoPhase1 = Frac(_lfoPhase1 + 0.043f * dt); // ~23s cycle
            _lfoPhase2 = Frac(_lfoPhase2 + 0.071f * dt); // ~14s cycle
            _lfoPhase3 = Frac(_lfoPhase3 + 0.113f * dt); // ~8.8s cycle

            float lfo1 = MathF.Sin(_lfoPhase1 * twoPi);
            float lfo2 = MathF.Sin(_lfoPhase2 * twoPi);
            float lfo3 = MathF.Sin(_lfoPhase3 * twoPi);

            float root = _curRoot;

            // ---- Layer 1: Sub-bass drone (fundamental + octave with 0.16 Hz beating) ----
            _subPhase1 = Frac(_subPhase1 + root * dt);
            _subPhase2 = Frac(_subPhase2 + (root * 2f + 0.16f) * dt);
            float sub = MathF.Sin(_subPhase1 * twoPi) * 0.65f
                      + MathF.Sin(_subPhase2 * twoPi) * 0.35f;
            sub = MathF.Tanh(sub * 1.35f) * 0.22f;

            // ---- Layer 2: Dark Unison Pad (3 notes x 3 voices = 9 oscillators) ----
            float padRawL = 0f;
            float padRawR = 0f;
            int pIdx = 0;
            for (int n = 0; n < 3; n++)
            {
                float baseFreq = root * chordMul[n];
                // Subtle slow pitch drift on the upper interval.
                if (n == 2) baseFreq *= 1f + lfo1 * 0.003f;

                for (int v = 0; v < 3; v++, pIdx++)
                {
                    float freq = baseFreq * detune[v];
                    float ph = Frac(_padPhases[pIdx] + freq * dt);
                    _padPhases[pIdx] = ph;

                    // Warm saw-triangle hybrid wave.
                    float saw = 2f * ph - 1f;
                    float tri = 1f - 4f * MathF.Abs(ph - 0.5f);
                    float sig = saw * 0.55f + tri * 0.45f;

                    // Spread voices across stereo field.
                    float pan = 0.5f + (v - 1) * 0.32f + (n - 1) * 0.12f * lfo2;
                    padRawL += sig * (1f - pan);
                    padRawR += sig * pan;
                }
            }

            // Tension layer: dissonant minor second / tritone growl when enemies are close.
            if (_tension > 0.005f)
            {
                _tensionPhase1 = Frac(_tensionPhase1 + (root * 2.118f) * dt); // Phrygian b2
                _tensionPhase2 = Frac(_tensionPhase2 + (root * 2.828f + 0.35f) * dt); // Tritone
                float tSig = (2f * _tensionPhase1 - 1f) * 0.5f + (2f * _tensionPhase2 - 1f) * 0.5f;
                tSig *= _tension * 0.9f;
                padRawL += tSig * 0.6f;
                padRawR += tSig * 0.6f;
            }

            // Exit-open layer: ethereal high fifth/octave shimmer when all gold is collected.
            if (_exitOpen > 0.005f)
            {
                _hopePhase1 = Frac(_hopePhase1 + (root * 6.0f + 0.12f) * dt);
                _hopePhase2 = Frac(_hopePhase2 + (root * 8.0f - 0.15f) * dt);
                float hSig = (MathF.Sin(_hopePhase1 * twoPi) + MathF.Sin(_hopePhase2 * twoPi)) * 0.45f * _exitOpen;
                padRawL += hSig * 0.55f;
                padRawR += hSig * 0.45f;
            }

            padRawL *= 0.065f;
            padRawR *= 0.065f;

            // Resonant State Variable Low-Pass Filter for the pad.
            // Cutoff breathes with LFOs, opens with tension/exitOpen, and closes when muffled in menus.
            float cutoffHz = 155f + 85f * (0.5f + 0.5f * lfo1) + 45f * lfo2
                           + _tension * 210f + _exitOpen * 160f;
            cutoffHz *= 1f - 0.42f * _muffle;
            float fPad = Math.Clamp(2f * MathF.Sin(MathF.PI * cutoffHz * dt), 0.005f, 0.45f);
            const float qPad = 0.65f; // gentle dark resonance

            float hpL = padRawL - _padLpL - qPad * _padBpL;
            _padBpL += fPad * hpL;
            _padLpL += fPad * _padBpL;

            float hpR = padRawR - _padLpR - qPad * _padBpR;
            _padBpR += fPad * hpR;
            _padLpR += fPad * _padBpR;

            // ---- Layer 3: Subterranean Cavern Wind (filtered Brownian/pink noise) ----
            float whiteL = (float)(_rng.NextDouble() * 2.0 - 1.0);
            float whiteR = (float)(_rng.NextDouble() * 2.0 - 1.0);
            _brownL = Math.Clamp(_brownL * 0.985f + whiteL * 0.04f, -1f, 1f);
            _brownR = Math.Clamp(_brownR * 0.985f + whiteR * 0.04f, -1f, 1f);

            float windHz = 220f + 130f * (0.5f + 0.5f * lfo2) + 60f * lfo3;
            float fWind = Math.Clamp(2f * MathF.Sin(MathF.PI * windHz * dt), 0.005f, 0.35f);
            const float qWind = 0.28f;

            float wHpL = _brownL - _windLpL - qWind * _windBpL;
            _windBpL += fWind * wHpL;
            _windLpL += fWind * _windBpL;

            float wHpR = _brownR - _windLpR - qWind * _windBpR;
            _windBpR += fWind * wHpR;
            _windLpR += fWind * _windBpR;

            float windGain = 0.045f * (0.65f + 0.35f * lfo3) * (1f - 0.35f * _muffle);
            float windL = _windBpL * windGain;
            float windR = _windBpR * windGain;

            // ---- Layer 4: Distant Cavern FM Bell / Metallic Toll ----
            _bellTimer -= dt;
            if (_bellTimer <= 0f)
            {
                _bellTimer = 11f + (float)_rng.NextDouble() * 14f;
                // Choose a haunting harmonic (fifth, minor sixth, minor seventh, or ninth in a higher octave).
                float[] bellHarms = [6.0f, 6.35f, 7.12f, 8.0f, 9.0f];
                _bellFreq = root * bellHarms[_rng.Next(bellHarms.Length)];
                _bellPan = 0.25f + (float)_rng.NextDouble() * 0.50f;
                _bellEnv = 1.0f;
            }

            float bellL = 0f, bellR = 0f;
            if (_bellEnv > 0.0005f)
            {
                _bellEnv *= 0.999962f; // ~6.5 second exponential decay
                // Inharmonic modulator ratio (1 : 1.414) gives a dark hollow bronze/stone chime.
                _bellPhaseM = Frac(_bellPhaseM + _bellFreq * 2.414f * dt);
                float mod = MathF.Sin(_bellPhaseM * twoPi) * _bellEnv * 0.45f;
                _bellPhaseC = Frac(_bellPhaseC + _bellFreq * dt);
                float bellSig = MathF.Sin(_bellPhaseC * twoPi + mod) * _bellEnv * _bellEnv * 0.040f * (1f - 0.5f * _muffle);
                bellL = bellSig * (1f - _bellPan);
                bellR = bellSig * _bellPan;
            }

            // ---- Layer 5: Mix + Cavern Delay & All-Pass Diffusion Reverb ----
            float dryL = sub + _padLpL + windL + bellL;
            float dryR = sub + _padLpR + windR + bellR;

            // Read stereo cross-delay lines.
            float dOutL = _delayL[_dlIdx];
            float dOutR = _delayR[_drIdx];

            // Dampen high frequencies in the delay feedback loop for a dark stone echo.
            _delayLpL += 0.08f * (dOutL - _delayLpL);
            _delayLpR += 0.08f * (dOutR - _delayLpR);

            // Ping-pong cross-feedback into the delay lines.
            _delayL[_dlIdx] = PadAndBellSend(dryL, bellL) + _delayLpR * 0.62f;
            _delayR[_drIdx] = PadAndBellSend(dryR, bellR) + _delayLpL * 0.62f;
            if (++_dlIdx >= _delayL.Length) _dlIdx = 0;
            if (++_drIdx >= _delayR.Length) _drIdx = 0;

            // Two cascaded Schroeder all-pass diffusers per channel.
            float wetL = AllPass(_ap2L, ref _ap2LIdx, AllPass(_ap1L, ref _ap1LIdx, _delayLpL, 0.55f), 0.50f);
            float wetR = AllPass(_ap2R, ref _ap2RIdx, AllPass(_ap1R, ref _ap1RIdx, _delayLpR, 0.55f), 0.50f);

            float outL = MathF.Tanh((dryL + wetL * 0.48f) * 0.85f) * _masterEnv;
            float outR = MathF.Tanh((dryR + wetR * 0.48f) * 0.85f) * _masterEnv;
            if (_masterEnv < 1f) _masterEnv = Math.Min(1f, _masterEnv + dt * 0.35f);

            buf[i * 2] = outL;
            buf[i * 2 + 1] = outR;
        }
    }

    private static float PadAndBellSend(float dry, float bell) => dry * 0.45f + bell * 1.1f;

    private static float AllPass(float[] buffer, ref int index, float input, float gain)
    {
        float delayed = buffer[index];
        float v = input - gain * delayed;
        buffer[index] = v;
        if (++index >= buffer.Length) index = 0;
        return delayed + gain * v;
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    public void Dispose()
    {
        if (!_valid) return;
        AudioDiag.Flush();
        Raylib.StopAudioStream(_stream);
        Raylib.UnloadAudioStream(_stream);
    }
}
