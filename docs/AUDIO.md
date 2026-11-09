# Procedural audio

The game ships with **no audio files**. Every sound effect and the background music are synthesised in memory
at startup (or in real time) by code in `src/LodeRunner.Game/Audio/`. `LodeRunner.Core` knows nothing about audio;
it only emits simulation events (`WorldEvent`, player pose, positions) that `AudioManager` turns into sounds.

## Files

| File | Role |
|------|------|
| `AudioManager.cs` | Owns the audio device, bakes all variants at startup, maps simulation events to sounds, picks idle variants, distance-based footsteps, volume. |
| `DarkAmbientSynth.cs` | Real-time ambient music stream (never repeats). Sub-buffer is 4096 frames (~93 ms) so a late frame cannot underrun; playback starts only after the first chunk is pushed. |
| `AudioDiag.cs` | Dev diagnostic (`--audio-diag`): logs stream feed timing and GC to `audio-diag.log`. |
| `ProceduralFootsteps.cs` | Run/climb/rope/guard steps, dig blast, WAV container + Raylib upload helpers (`ToWav`, `LoadWav`). |
| `ProceduralStone.cs` | Menu/pause sounds: heavy stone slabs in a cavern. |
| `ProceduralSfx.cs` | Formant voice synth, gold bell, fall cry, trap "oh!", death scream, game-over funeral toll. |
| `ProceduralSfx.Extra.cs` | Ladders reveal, banished-demon enemy death, choral win fanfare. |

## Pipeline

1. Each generator is a pure function `(int variant) -> byte[]` returning a stereo 16-bit 44.1 kHz PCM WAV.
   It uses a deterministic `Random` seeded from the variant index, so output is reproducible.
2. `AudioManager.LoadVariantSounds` renders every (sound, variant) pair with `Parallel.For`. This is pure DSP and
   makes no Raylib calls, so it is thread-safe. About 105 variants take roughly 2 s.
3. Results are uploaded sequentially on the main thread with `Raylib.LoadWaveFromMemory(".wav", bytes)` followed
   by `LoadSoundFromWave`.
4. Footsteps (run, climb, rope, guard; left and right foot) are created per kind and registered in a small pool of
   `LoadSoundAlias` aliases (`StepPoolSize`).

If no audio device is available, `AudioManager` stays inactive and every call is a no-op.

## Variant engine

Replaying a Raylib `Sound` restarts its playhead, which cuts off reverb tails. Each event therefore has a pool of
3 to 10 independently rendered variants. Each variant differs in pitch, formants, decay, timing and noise.

- `Play(id)` picks an idle variant (not currently playing), starting from a random index. If all are busy it
  reuses one.
- A slight random pitch jitter is applied on play.
- Consecutive repeats never sound identical, and overlapping events do not cut each other off.

Variant counts: most sounds 10, `Death` and `EnemyDie` and menu sounds 8, `Win` 4, `GameOver` 3.

## Synthesis techniques

- **Footsteps**: low-passed noise thump plus a short body resonance, per-surface character. Ladder rungs are a dull
  steel resonance (about 205 and 228 Hz) with low-passed contact knocks. Rope grips are creaky friction noise.
- **Stone UI** (`ProceduralStone`): each sound is one or more slab impacts (sub thump, inharmonic stone modes, muffled
  grit) fed into a damped multi-tap comb plus all-pass reverb for a cavern feel.
- **Gold**: inharmonic bell partials around 2.35 kHz, detuned pairs for shimmer.
- **Voice** (`ProceduralSfx`): formant synthesis. A differentiated glottal pulse train goes through three parallel
  band-pass biquads (F1, F2, F3), with aspiration noise, pitch tremor and saturation drive. Filter coefficients are
  recomputed every 8 samples (`(i & 7) == 0`) because formants move slowly; this is a large speed-up with no audible
  artefacts.
  - `Fall`: quiet muffled male cry, rising pitch with vibrato.
  - `Trap`: breathy lax-phonation "oh!".
  - `Death`: strained scream tearing upward with voice crack. When the hero is bricked into a hole it starts as soon as the hole begins to refill (`AudioManager.OnHeroBricking`), not when it closes; it plays once per world.
  - `EnemyDie`: banished demon: sub and tritone voices, spirit wail, breath, gate slam.
- **Bells** (`GameOver`): 8 s funeral toll, minor third, church-hall resonance (modal synthesis plus reverb).
- **Choir** (`Win`): C major fanfare, 4 to 6 singers per chord, pitch scoop, delayed vibrato, hall reverb.
- **Ladders**: three rising metal taps on a steel rung.
- **Ambient** (`DarkAmbientSynth`): five layers rendered in real time into a Raylib `AudioStream`: beating sub-bass
  drone, minor/Phrygian pad through a breathing resonant filter, cavern wind noise, rare distant FM bells, and stereo
  cross-feedback delay with all-pass diffusion. It reacts to enemy proximity (tension), exit reveal (shimmer) and menus
  (muffle).

## Levels and mixing

- Per-sound gain lives in `AudioManager.Gain` (footsteps are kept quiet; menu move is 0.62).
- `Fall`, `Death` and `EnemyDie` are deliberately muffled and quiet (generator levels about 0.20 to 0.55) so that
  frequent repeats do not fatigue the player.
- `Volume` (settings) scales all effects; `MusicVolume` scales the ambient stream.

## Adding a sound

1. Add a member to `Sfx`.
2. Write `public static byte[] CreateX(int variant = 0)` and seed randomness from `variant`.
3. Add a recipe `(Sfx.X, count, CreateX)` to `LoadVariantSounds`.
4. Trigger it from `AudioManager` where the matching event is handled.

Keep generators free of Raylib calls so they stay safe for `Parallel.For`.
