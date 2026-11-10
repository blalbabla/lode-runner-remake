using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Audio;

/// <summary>
/// Plays procedurally synthesised sound effects for simulation events. A missing audio device
/// silently disables sound; audio must never stop the game from starting.
/// </summary>
internal sealed class AudioManager : IDisposable
{

    private static readonly (Sfx Id, ProceduralFootsteps.StepKind Kind)[] ProceduralSteps =
    [
        (Sfx.Step1, ProceduralFootsteps.StepKind.RunLeft),
        (Sfx.Step2, ProceduralFootsteps.StepKind.RunRight),
        (Sfx.Climb1, ProceduralFootsteps.StepKind.ClimbLeft),
        (Sfx.Climb2, ProceduralFootsteps.StepKind.ClimbRight),
        (Sfx.Rope1, ProceduralFootsteps.StepKind.RopeLeft),
        (Sfx.Rope2, ProceduralFootsteps.StepKind.RopeRight),
        (Sfx.GuardStep1, ProceduralFootsteps.StepKind.GuardLeft),
        (Sfx.GuardStep2, ProceduralFootsteps.StepKind.GuardRight),
    ];

    private static readonly (Sfx Id, ProceduralStone.UiKind Kind)[] ProceduralUi =
    [
        (Sfx.MenuMove, ProceduralStone.UiKind.Move),
        (Sfx.MenuSelect, ProceduralStone.UiKind.Select),
        (Sfx.MenuBack, ProceduralStone.UiKind.Back),
        (Sfx.Pause, ProceduralStone.UiKind.Pause),
    ];

    // Quiet or repetitive sounds are played below full volume so they do not drown out the events.
    private static readonly Dictionary<Sfx, float> Gain = new()
    {
        [Sfx.Step1] = 0.52f, [Sfx.Step2] = 0.52f,
        [Sfx.Climb1] = 0.48f, [Sfx.Climb2] = 0.48f,
        [Sfx.Rope1] = 0.44f, [Sfx.Rope2] = 0.44f,
        [Sfx.GuardStep1] = 0.36f, [Sfx.GuardStep2] = 0.36f,
        [Sfx.MenuMove] = 0.62f,
    };

    // Distance travelled (in simulation units, where 1 tile = 48 units) between two footsteps —
    // matched to CharacterRig stride & ladder rung spacing (12 units = 1/4 tile = 1 ladder rung).
    private const int RunStepUnits = 28;
    private const int ClimbStepUnits = 12;
    private const int RopeStepUnits = 24;
    private const int GuardStepUnits = 30;

    // Pre-rendered variants per event sound (slightly different pitch/timing/decay); one is picked at random per play.
    private readonly Dictionary<Sfx, Sound[]> _variants = [];
    private readonly Dictionary<Sfx, StereoClip[]> _clips = [];
    private readonly Dictionary<Sfx, StereoClip[]> _stepClips = [];
    private readonly Dictionary<Sfx, Sound[]> _fallbackSteps = [];
    private readonly Dictionary<Sfx, int> _fallbackStepCursor = [];
    private readonly Dictionary<int, int> _enemyStepDist = [];
    private readonly Random _pitchRng = new(42);
    private readonly bool _ready;
    private readonly DarkAmbientSynth _music;
    private readonly ProceduralMixer _mixer;
    private float _sfxVolume = 0.7f;
    private bool _playerFalling;
    private bool _screamed; // the death scream already played for this world
    private int _stepDistance;
    private bool _stepFlip;
    private bool _guardStepFlip;
    private Pose _stepPose = Pose.Stand;
    private Cell _listenerCell;
    private float _listenerYaw;
    private bool _firstPersonListener;
    private float _musicDuck;

    /// <param name="progress">Optional loading callback: overall fraction 0..1 and a status label.</param>
    public AudioManager(Action<float, string>? progress = null)
    {
        Raylib.InitAudioDevice();
        _ready = Raylib.IsAudioDeviceReady();
        _mixer = new ProceduralMixer(_ready);
        _music = new DarkAmbientSynth(!_mixer.IsValid);
        if (_mixer.IsValid) _mixer.SetMusicSource(_music);
        if (!_ready) return;

        LoadVariantSounds(progress);

        progress?.Invoke(0.92f, "Creating footstep sounds...");
        LoadFootstepClips();

        ApplySfxVolumes();
    }

    // How many pre-rendered variants each event sound gets (long sounds cost more to render, so fewer).
    private const int DefaultVariants = 10;

    /// <summary>
    /// Renders every event sound several times with small random-but-deterministic differences, in parallel.
    /// While the workers run, the main thread keeps reporting progress so the loading screen stays alive.
    /// </summary>
    private void LoadVariantSounds(Action<float, string>? progress)
    {
        var recipes = new List<(Sfx Id, int Count, Func<int, StereoClip> Make)>
        {
            (Sfx.Gold, DefaultVariants, ProceduralSfx.CreateGold),
            (Sfx.Dig, DefaultVariants, ProceduralFootsteps.CreateDig),
            (Sfx.Fall, DefaultVariants, ProceduralSfx.CreateFall),
            (Sfx.Trap, DefaultVariants, ProceduralSfx.CreateTrap),
            (Sfx.Death, 8, ProceduralSfx.CreateDeath),
            (Sfx.GameOver, 3, ProceduralSfx.CreateGameOver),
            (Sfx.Ladders, DefaultVariants, ProceduralSfx.CreateLadders),
            (Sfx.EnemyDie, 8, ProceduralSfx.CreateEnemyDie),
            (Sfx.Win, 4, ProceduralSfx.CreateWin),
        };
        foreach (var (id, kind) in ProceduralUi)
        {
            var k = kind;
            recipes.Add((id, 8, v => ProceduralStone.Create(k, v)));
        }

        var jobs = new List<(Sfx Id, int Variant, Func<int, StereoClip> Make)>();
        foreach (var (id, count, make) in recipes)
            for (int v = 0; v < count; v++)
                jobs.Add((id, v, make));

        var rendered = new StereoClip[jobs.Count];
        int done = 0;
        var render = Task.Run(() => Parallel.For(0, jobs.Count, i =>
        {
            rendered[i] = jobs[i].Make(jobs[i].Variant);
            Interlocked.Increment(ref done);
        }));
        while (!render.IsCompleted)
        {
            progress?.Invoke(0.05f + 0.80f * Volatile.Read(ref done) / jobs.Count, "Synthesizing sound effects...");
            Thread.Sleep(1);
        }
        render.GetAwaiter().GetResult(); // rethrow any synthesis error

        progress?.Invoke(0.88f, "Preparing sound mixer...");
        var loaded = new Dictionary<Sfx, List<Sound>>();
        var clips = new Dictionary<Sfx, List<StereoClip>>();
        for (int i = 0; i < jobs.Count; i++)
        {
            var clip = rendered[i];
            if (_mixer.IsValid)
            {
                if (!clips.TryGetValue(jobs[i].Id, out var clipList)) clips[jobs[i].Id] = clipList = [];
                clipList.Add(clip);
                continue;
            }
            var sound = ProceduralFootsteps.ToSound(clip);
            if (!Raylib.IsSoundValid(sound)) continue;
            if (!loaded.TryGetValue(jobs[i].Id, out var list)) loaded[jobs[i].Id] = list = [];
            list.Add(sound);
        }

        foreach (var (id, list) in loaded) _variants[id] = [.. list];
        foreach (var (id, list) in clips) _clips[id] = [.. list];
    }

    private void LoadFootstepClips()
    {
        const int variants = 5;
        foreach (var (id, kind) in ProceduralSteps)
        {
            if (!_mixer.IsValid)
            {
                var sound = ProceduralFootsteps.Create(kind);
                if (!Raylib.IsSoundValid(sound)) continue;
                var pool = new Sound[4];
                pool[0] = sound;
                for (int i = 1; i < pool.Length; i++) pool[i] = Raylib.LoadSoundAlias(sound);
                _fallbackSteps[id] = pool;
                continue;
            }
            var clips = new List<StereoClip>(variants);
            for (int variant = 0; variant < variants; variant++)
            {
                clips.Add(ProceduralFootsteps.CreateClip(kind, variant));
            }
            if (clips.Count > 0) _stepClips[id] = [.. clips];
        }
    }

    /// <summary>Sound effects volume, 0..1.</summary>
    public float Volume
    {
        get => _sfxVolume;
        set
        {
            _sfxVolume = Math.Clamp(value, 0f, 1f);
            if (_ready) ApplySfxVolumes();
        }
    }

    /// <summary>Procedural dark ambient music volume, 0..1.</summary>
    public float MusicVolume
    {
        get => _music.Volume;
        set => _music.Volume = value;
    }

    private void ApplySfxVolumes()
    {
        foreach (var (id, set) in _variants)
        {
            float baseGain = Gain.TryGetValue(id, out float g) ? g : 1f;
            foreach (var sound in set)
                Raylib.SetSoundVolume(sound, baseGain * _sfxVolume);
        }
        foreach (var (id, pool) in _fallbackSteps)
        {
            float baseGain = Gain.TryGetValue(id, out float g) ? g : 1f;
            foreach (var sound in pool) Raylib.SetSoundVolume(sound, baseGain * _sfxVolume);
        }
    }

    /// <summary>Feeds the procedural dark ambient synthesizer stream once per frame.</summary>
    public void UpdateMusic(GameWorld world, bool gameHeld, int levelIndex, float dt)
    {
        _musicDuck = Math.Max(0f, _musicDuck - dt * 1.6f);
        _music.SetDucking(1f - _musicDuck);
        AudioDiag.BeginUpdate(gameHeld);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _music.Update(world, gameHeld, levelIndex);
        _mixer.Update();
        AudioDiag.EndUpdate(_mixer.LastChunks, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            _mixer.LastPeak, _mixer.LastLimiterReduction, _mixer.ActiveVoices);
    }

    /// <summary>Updates listener state before sounds from the next simulation tick are emitted.</summary>
    public void UpdateListener(GameWorld world, float yaw, bool firstPerson)
    {
        _listenerCell = new Cell(
            Math.Clamp((world.Player.X + world.Grid.TileUnits / 2) / world.Grid.TileUnits, 0, world.Grid.Cols - 1),
            Math.Clamp((world.Player.Y + world.Grid.TileUnits / 2) / world.Grid.TileUnits, 0, world.Grid.Rows - 1));
        _listenerYaw = yaw;
        _firstPersonListener = firstPerson;
    }

    /// <summary>Live-updates the music mixer levels (0..1 each). Takes effect on the next synthesized chunk.</summary>
    public void SetMixerLevels(float bass, float pad, float wind, float bells)
        => _music.SetMixerLevels(bass, pad, wind, bells);

    /// <summary>
    /// The hero is being walled in: the death scream starts now, while the bricks close over him, instead of
    /// after the hole has shut. Plays once per world; the later PlayerDied event then skips the scream.
    /// </summary>
    public void OnHeroBricking()
    {
        if (_screamed) return;
        _screamed = true;
        StopFall();
        Play(Sfx.Death);
    }

    /// <summary>Resets edge detection; call when a new world is created.</summary>
    public void Reset()
    {
        _screamed = false;
        StopFall();
        _playerFalling = false;
        _stepDistance = 0;
        _stepPose = Pose.Stand;
        _enemyStepDist.Clear();
    }

    /// <summary>Plays the sounds for one tick: its events, plus the player's fall start/landing.</summary>
    /// <param name="gameOver">True once the campaign has no lives left (replaces the death sound).</param>
    public void OnTick(GameWorld world, bool gameOver)
    {
        foreach (var ev in world.Events)
        {
            switch (ev.Kind)
            {
                case WorldEventKind.GoldCollected: PlayAt(Sfx.Gold, ev.Cell, world.Grid); break;
                case WorldEventKind.HoleOpened: PlayAt(Sfx.Dig, ev.Cell, world.Grid); break;
                case WorldEventKind.LaddersRevealed: Play(Sfx.Ladders); break;
                case WorldEventKind.EnemyTrapped: PlayAt(Sfx.Trap, ev.Cell, world.Grid); break;
                case WorldEventKind.EnemyDied: PlayAt(Sfx.EnemyDie, ev.Cell, world.Grid); break;
                case WorldEventKind.PlayerDied:
                    StopFall();
                    if (gameOver) Play(Sfx.GameOver);
                    else if (!_screamed) Play(Sfx.Death); // already screamed while being walled in
                    _screamed = true;
                    break;
                case WorldEventKind.LevelWon:
                    StopFall();
                    Play(Sfx.Win);
                    break;
            }
        }

        bool falling = world.Phase == GamePhase.Playing && world.Player.Falling;
        if (falling && !_playerFalling) Play(Sfx.Fall);
        else if (!falling && _playerFalling) StopFall();
        _playerFalling = falling;

        PlayFootsteps(world);
        PlayEnemyFootsteps(world);
    }

    /// <summary>Alternating step/rung sounds, one per fixed distance travelled on foot, ladder or rope.</summary>
    private void PlayFootsteps(GameWorld world)
    {
        var p = world.Player;
        if (world.Phase != GamePhase.Playing || p.Falling || !p.Moved)
        {
            _stepDistance = 0;
            _stepPose = Pose.Stand;
            return;
        }

        var (threshold, first, second) = p.Pose switch
        {
            Pose.Run => (RunStepUnits, Sfx.Step1, Sfx.Step2),
            Pose.Climb => (ClimbStepUnits, Sfx.Climb1, Sfx.Climb2),
            Pose.Hang => (RopeStepUnits, Sfx.Rope1, Sfx.Rope2),
            _ => (0, Sfx.Step1, Sfx.Step2),
        };
        if (threshold == 0) { _stepDistance = 0; _stepPose = Pose.Stand; return; }

        if (p.Pose != _stepPose)
        {
            _stepPose = p.Pose;
            _stepDistance = threshold; // step right away when starting a new kind of movement
        }

        _stepDistance += Math.Abs(p.X - p.PrevX) + Math.Abs(p.Y - p.PrevY);
        if (_stepDistance < threshold) return;

        _stepDistance %= threshold;
        _stepFlip = !_stepFlip;

        PlayStep(_stepFlip ? first : second, 1f, 0.5f, 0f);
    }

    /// <summary>Quiet distance-attenuated and stereo-panned footsteps for active guards.</summary>
    private void PlayEnemyFootsteps(GameWorld world)
    {
        if (world.Phase != GamePhase.Playing) return;

        foreach (var e in world.Enemies)
        {
            if (!e.IsAlive || e.State != EnemyState.Active || e.Falling || !e.Moved)
            {
                _enemyStepDist[e.Id] = 0;
                continue;
            }

            int distUnits = _enemyStepDist.GetValueOrDefault(e.Id) + Math.Abs(e.X - e.PrevX) + Math.Abs(e.Y - e.PrevY);
            int threshold = e.Pose == Pose.Climb ? ClimbStepUnits * 2 : GuardStepUnits;
            if (distUnits < threshold)
            {
                _enemyStepDist[e.Id] = distUnits;
                continue;
            }

            _enemyStepDist[e.Id] = distUnits % threshold;

            var source = new Cell(
                Math.Clamp((e.X + world.Grid.TileUnits / 2) / world.Grid.TileUnits, 0, world.Grid.Cols - 1),
                Math.Clamp((e.Y + world.Grid.TileUnits / 2) / world.Grid.TileUnits, 0, world.Grid.Rows - 1));
            var mix = _firstPersonListener
                ? SpatialAudio.Evaluate(world.Grid, _listenerCell, source, _listenerYaw, true)
                : new SpatialMix(0.18f, 0.5f, 0f);
            if (mix.Gain < 0.05f) continue;

            _guardStepFlip = !_guardStepFlip;
            Sfx sfx = e.Pose == Pose.Climb
                ? (_guardStepFlip ? Sfx.Climb1 : Sfx.Climb2)
                : (_guardStepFlip ? Sfx.GuardStep1 : Sfx.GuardStep2);
            PlayStep(sfx, mix.Gain * 0.78f, mix.Pan, mix.Occlusion);
        }
    }

    private void PlayStep(Sfx id, float gainScale, float pan, float occlusion)
    {
        float baseGain = Gain.TryGetValue(id, out float g) ? g : 1f;
        float pitch = 0.96f + (float)_pitchRng.NextDouble() * 0.08f;
        if (_stepClips.TryGetValue(id, out var clips))
        {
            var clip = clips[_pitchRng.Next(clips.Length)];
            _mixer.Play(id, clip, baseGain * _sfxVolume * gainScale, pan, pitch, occlusion);
            return;
        }
        if (!_fallbackSteps.TryGetValue(id, out var pool)) return;
        int cursor = _fallbackStepCursor.GetValueOrDefault(id);
        _fallbackStepCursor[id] = (cursor + 1) % pool.Length;
        var sound = pool[cursor];
        Raylib.SetSoundVolume(sound, baseGain * _sfxVolume * gainScale);
        Raylib.SetSoundPitch(sound, pitch - occlusion * 0.03f);
        Raylib.SetSoundPan(sound, pan);
        Raylib.PlaySound(sound);
    }

    /// <summary>Silences looping or long sounds, e.g. when a menu opens mid-fall.</summary>
    public void StopFall()
    {
        _mixer.Stop(Sfx.Fall);
        if (_variants.TryGetValue(Sfx.Fall, out var set))
            foreach (var s in set) Raylib.StopSound(s);
    }

    public void Play(Sfx id)
    {
        if (_clips.TryGetValue(id, out var clips))
        {
            var clip = clips[_pitchRng.Next(clips.Length)];
            _mixer.Play(id, clip, Gain.TryGetValue(id, out float clipGain) ? clipGain * _sfxVolume : _sfxVolume,
                0.5f, 0.975f + (float)_pitchRng.NextDouble() * 0.05f, 0f);
            DuckMusic(id);
            return;
        }

        if (_variants.TryGetValue(id, out var set))
        {
            // Random variant, preferring one that is not still ringing so rapid repeats overlap instead of cutting off.
            int start = _pitchRng.Next(set.Length);
            var pick = set[start];
            for (int k = 0; k < set.Length; k++)
            {
                var cand = set[(start + k) % set.Length];
                if (!Raylib.IsSoundPlaying(cand)) { pick = cand; break; }
            }

            float gain = Gain.TryGetValue(id, out float vg) ? vg : 1f;
            Raylib.SetSoundVolume(pick, gain * _sfxVolume);
            Raylib.SetSoundPitch(pick, 0.975f + (float)_pitchRng.NextDouble() * 0.05f);
            Raylib.SetSoundPan(pick, 0.5f);
            Raylib.PlaySound(pick);
            DuckMusic(id);
            return;
        }

    }

    private void PlayAt(Sfx id, Cell source, Grid grid)
    {
        if (!_firstPersonListener)
        {
            Play(id);
            return;
        }
        var mix = SpatialAudio.Evaluate(grid, _listenerCell, source, _listenerYaw, _firstPersonListener);
        Play(id, mix.Gain, mix.Pan, mix.Occlusion);
    }

    private void Play(Sfx id, float gainScale, float pan, float occlusion)
    {
        if (_clips.TryGetValue(id, out var clips))
        {
            var clip = clips[_pitchRng.Next(clips.Length)];
            float clipGain = Gain.TryGetValue(id, out float clipBaseGain) ? clipBaseGain : 1f;
            _mixer.Play(id, clip, clipGain * _sfxVolume * gainScale, pan,
                0.975f + (float)_pitchRng.NextDouble() * 0.05f, occlusion);
            DuckMusic(id);
            return;
        }

        if (!_variants.TryGetValue(id, out var set))
        {
            Play(id);
            return;
        }

        int start = _pitchRng.Next(set.Length);
        var pick = set[start];
        for (int k = 0; k < set.Length; k++)
        {
            var candidate = set[(start + k) % set.Length];
            if (!Raylib.IsSoundPlaying(candidate)) { pick = candidate; break; }
        }

        float gain = Gain.TryGetValue(id, out float value) ? value : 1f;
        Raylib.SetSoundVolume(pick, gain * _sfxVolume * gainScale);
        Raylib.SetSoundPitch(pick, 0.975f + (float)_pitchRng.NextDouble() * 0.05f - occlusion * 0.03f);
        Raylib.SetSoundPan(pick, pan);
        Raylib.PlaySound(pick);
        DuckMusic(id);
    }

    private void DuckMusic(Sfx id)
    {
        float amount = id switch
        {
            Sfx.Dig or Sfx.Fall or Sfx.Trap => 0.14f,
            Sfx.Death or Sfx.GameOver or Sfx.EnemyDie => 0.30f,
            Sfx.Win => 0.22f,
            _ => 0f,
        };
        _musicDuck = Math.Max(_musicDuck, amount);
    }

    public void Dispose()
    {
        if (!_ready) return;
        _music.Dispose();
        _mixer.Dispose();
        foreach (var set in _variants.Values)
            foreach (var s in set) Raylib.UnloadSound(s);
        foreach (var pool in _fallbackSteps.Values)
        {
            for (int i = 1; i < pool.Length; i++) Raylib.UnloadSoundAlias(pool[i]);
            Raylib.UnloadSound(pool[0]);
        }
        Raylib.CloseAudioDevice();
    }
}
