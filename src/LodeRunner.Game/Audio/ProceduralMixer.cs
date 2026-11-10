using Raylib_cs;

namespace LodeRunner.Game.Audio;

internal sealed class StereoClip
{
    public StereoClip(float[] left, float[] right)
    {
        Left = left;
        Right = right;
    }

    public float[] Left { get; }
    public float[] Right { get; }
    public int Frames => Left.Length;
}

/// <summary>Float mixer for procedural effects with voice limiting and per-voice filtering.</summary>
internal sealed class ProceduralMixer : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BufferFrames = 2048;
    private const int MaxVoices = 24;

    private readonly AudioStream _stream;
    private readonly bool _valid;
    private readonly float[] _buffer = new float[BufferFrames * Channels];
    private readonly List<Voice> _voices = new(MaxVoices);
    private DarkAmbientSynth? _music;
    private bool _started;

    public bool IsValid => _valid;
    public int ActiveVoices => _voices.Count;
    public float LastPeak { get; private set; }
    public float LastLimiterReduction { get; private set; }
    public int LastChunks { get; private set; }

    public void SetMusicSource(DarkAmbientSynth music) => _music = music;

    private struct Voice
    {
        public StereoClip Clip;
        public Sfx Tag;
        public float Position;
        public float Rate;
        public float GainLeft;
        public float GainRight;
        public float Filter;
        public float LowLeft;
        public float LowRight;
    }

    public ProceduralMixer(bool audioReady)
    {
        if (!audioReady) return;

        Raylib.SetAudioStreamBufferSizeDefault(BufferFrames);
        _stream = Raylib.LoadAudioStream(SampleRate, 32, Channels);
        _valid = Raylib.IsAudioStreamValid(_stream);
    }

    public void Play(Sfx tag, StereoClip clip, float gain, float pan, float pitch, float occlusion)
    {
        if (!_valid || clip.Frames == 0) return;
        if (_voices.Count >= MaxVoices) _voices.RemoveAt(0);

        pan = Math.Clamp(pan, 0f, 1f);
        _voices.Add(new Voice
        {
            Clip = clip,
            Tag = tag,
            Rate = Math.Clamp(pitch, 0.5f, 2f),
            GainLeft = gain * (pan <= 0.5f ? 1f : 2f * (1f - pan)),
            GainRight = gain * (pan >= 0.5f ? 1f : 2f * pan),
            Filter = 0.46f - Math.Clamp(occlusion, 0f, 0.70f) * 0.38f,
        });
    }

    public void Stop(Sfx tag)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
            if (_voices[i].Tag == tag) _voices.RemoveAt(i);
    }

    public void Update()
    {
        if (!_valid || !Raylib.IsAudioDeviceReady()) return;
        LastChunks = 0;
        while (Raylib.IsAudioStreamProcessed(_stream))
        {
            MixChunk();
            Raylib.UpdateAudioStream(_stream, (ReadOnlySpan<float>)_buffer, BufferFrames);
            LastChunks++;
        }

        if (!_started)
        {
            Raylib.PlayAudioStream(_stream);
            _started = true;
        }
        else if (!Raylib.IsAudioStreamPlaying(_stream))
        {
            Raylib.ResumeAudioStream(_stream);
        }
    }

    private void MixChunk()
    {
        Array.Clear(_buffer);
        _music?.RenderInto(_buffer);
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var voice = _voices[i];
            int frame;
            for (frame = 0; frame < BufferFrames && voice.Position < voice.Clip.Frames - 1; frame++)
            {
                int index = (int)voice.Position;
                float fraction = voice.Position - index;
                float left = Lerp(voice.Clip.Left[index], voice.Clip.Left[index + 1], fraction);
                float right = Lerp(voice.Clip.Right[index], voice.Clip.Right[index + 1], fraction);
                voice.LowLeft += (left - voice.LowLeft) * voice.Filter;
                voice.LowRight += (right - voice.LowRight) * voice.Filter;
                int output = frame * Channels;
                _buffer[output] += voice.LowLeft * voice.GainLeft;
                _buffer[output + 1] += voice.LowRight * voice.GainRight;
                voice.Position += voice.Rate;
            }

            if (voice.Position >= voice.Clip.Frames - 1) _voices.RemoveAt(i);
            else _voices[i] = voice;
        }

        float peak = 0f;
        float reduction = 0f;
        for (int i = 0; i < _buffer.Length; i++)
        {
            float input = _buffer[i];
            float output = MathF.Tanh(input * 1.15f) / 1.15f;
            _buffer[i] = output;
            peak = MathF.Max(peak, MathF.Abs(output));
            reduction = MathF.Max(reduction, MathF.Abs(input) - MathF.Abs(output));
        }
        LastPeak = peak;
        LastLimiterReduction = reduction;
    }

    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;

    public void Dispose()
    {
        if (!_valid) return;
        Raylib.StopAudioStream(_stream);
        Raylib.UnloadAudioStream(_stream);
    }
}
