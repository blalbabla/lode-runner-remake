# Lode Runner 3D-side — design document

Status: v1.0 (MVP: M0–M6 implemented)
Stack: C# (.NET 10, SDK 10.0.302 installed) + Raylib-cs

## 1. Goals and scope

### 1.1 Goal
Reproduce the core gameplay of Lode Runner (Broderbund, 1983) with minimal simplifications. Visually the game looks like a side-view 2D platformer, but the world is built from real 3D primitives and filmed by a static perspective camera.

### 1.2 Principles
1. **Gameplay does not change.** No acceleration, jumping or inertia. Speeds are constant. Falling is uniform.
2. **Logic is separate from presentation.** All mechanics live in a project with no Raylib dependency. This makes the rules testable and lets us change the camera/renderer later without touching the simulation.
3. **Determinism.** The simulation works with integers and a fixed time step; randomness comes from a seeded RNG. The same input gives the same result.
4. **Core first.** Menus, score, lives and a level editor are outside the MVP.

### 1.3 MVP non-goals
- Menus, level select screen, score, lives, high scores
- Level editor
- Sound and music (only event points in the simulation are provided as a hook)
- False bricks (`X` in the original), multiplayer
- External 3D models and textures
- First-person mode, photorealism, human characters (see section 9)
- Exact copy of the original AI (see 2.6 — the AI is simplified)

## 2. Game rules

### 2.1 Level
- Grid of **28 × 16** cells (as in the original). Dimensions are stored in the level data, not hard-coded.
- Cell types:

| Symbol | Type | Properties |
|---|---|---|
| ` ` | Empty | passable, no support |
| `#` | Brick | impassable, support, **can be dug** |
| `@` | Concrete | impassable, support, **cannot be dug** |
| `H` | Ladder | passable, climb up/down, supports from above |
| `-` | Rope | passable, hang and move horizontally |
| `S` | Hidden ladder | empty until all gold is collected; then becomes `H` |
| `$` | Gold | lies on an empty cell/ladder/rope |
| `&` | Player start | marker, the cell is empty |
| `0` | Enemy start | marker, the cell is empty |

The symbols match the usual Lode Runner clone notation, so existing levels can be used without conversion.

### 2.2 Player
The player can **only**: run left/right, climb a ladder up/down, move along a rope, dig a hole down-left or down-right.

**Support states** (checked every tick, in priority order):
1. The player's cell is a ladder → climbing mode: stands, walks, climbs.
2. The player's cell is a rope → hangs. Left/right — moves along the rope. Down — lets go of the rope and falls. Up — nothing (unless the same cell also holds a ladder).
3. The cell below the feet is brick/concrete/ladder/enemy head → stands on support.
4. Otherwise — falls.

**Falling:** uniform downward speed, no acceleration. Horizontal control is ignored while falling. If the fall passes through a rope cell, the player grabs the rope (as in the original). A fall ends on any support from items 1–3: the player stops on a brick, concrete, the top of a ladder (above the ladder) or in a rope cell.

*Implementation (M2):* support is determined by the nearest column, so a player who walks off an edge by more than half a cell falls. If the player is offset from the column centre at that moment, they smoothly align to its centre while falling (no faster than running speed).

**Ladders and alignment:** vertical movement is possible only when `x` is aligned to the cell centre. If up/down is pressed and a ladder cell is nearby (offset less than half a cell), the player first moves horizontally to the centre of that cell at normal speed. Speeds do not change.

**Picking up gold:** the player picks up gold when the player's centre enters the gold cell.

**Digging:**
- Conditions: the player stands on support OR is on any rung of a ladder (`Pose.Climb`) OR is hanging on a rope (`Pose.Hang`). Not falling. Aligned in `y`; alignment in `x` is not required (auto-alignment, see below).
- The dig target cell `(c±1, r+1)` must be a brick. Cell `(c±1, r)` must be empty, with no gold and no enemy (it must not contain a brick, concrete or ladder).
- While digging, the player is stationary (`DigTicks`). The direction is fixed at the moment of the key press.

*Implementation (M3):*
- Digging from a ladder (any rung, ) and while hanging on a rope () is allowed -- matches original 1983 mechanics. Row is taken from  rounding so the target cell is always well-defined mid-rung or mid-rope.
- The target cell `(c±1, r)` must be exactly `Empty`. A rope, ladder, brick or concrete there forbids digging. A hidden ladder counts as empty until revealed.
- Digging beyond the level edge is not possible (outside the grid counts as concrete).
- While a dig is in progress, a new hole cannot be started. The hole appears immediately at the start of the dig (state `Opening`), not at the end. The cell becomes passable when `Opening` ends.
- Enemy check in the target cell: if a live enemy stands in `(c±1, r)`, digging is forbidden (M4).
- Auto-alignment: column `c` is taken as the one nearest to the player's centre (`(X + T/2) / T`). During the dig the player slides to the centre of that column at running speed without overshooting. A dig takes `DigTicks = 18` ticks, while the maximum offset `T/2 = 24` units at speed 4 is covered in 6 ticks, so by the end of the dig the player always stands exactly on the column.

### 2.3 Hole (brick life cycle)

```
Intact --dig--> Opening --(DigTicks)--> Open --(HoleOpenTicks)--> Refilling --(RefillTicks)--> Intact
```
- `Opening` and `Open` — the cell is passable and can be fallen into. In `Refilling` the cell already counts as a brick for support, but a creature inside still dies when it completes.
- On the transition to `Intact`, any creature inside the cell **dies**: the player — the level is lost, an enemy — respawn (see 2.5).
- A hole is one cell deep. A second hole cannot be dug directly above it, because condition 2.2 requires a brick at `(c±1, r+1)`.
- It is impossible to climb out of a hole sideways: the walls are bricks. The player leaves a hole upward only via a ladder; if there is none, the player waits for the hole to close and dies. Enemies have a separate rule (2.5).

*Implementation (M3):* the tick order is: hole timers first (a closing hole kills the player inside), then the player, then gold pickup. After a loss (`GamePhase.Lost`) the world freezes: `Tick` changes nothing until a new `GameWorld` is created. Hole states and events (`DigStarted`, `HoleOpened`, `HoleRefilling`, `HoleClosed`, `GoldCollected`, `LaddersRevealed`, `PlayerDied`) are available through `Grid.Hole(...)` and `GameWorld.Events`.

### 2.4 Gold and victory
- The exit opens after **all** gold is collected: every `S` turns into `H`.
- It is enough to climb them to the top row (row 0) and stand there aligned in `y`. This completes the level.
- Defeat: the player dies from contact with an enemy or a closing hole.
- End of level (win/loss) in the MVP — a message screen and a restart of the same level on a key press.

*Implementation (M5):*
- Victory: `GamePhase.Won` and the `LevelWon` event when `GoldRemaining == 0` and `Player.Y == 0`. The check runs right after gold pickup and before enemies move, so a player who reaches the top wins even if an enemy would have touched them in the same tick. The last gold in the top row gives victory at the moment of pickup.
- Gold carried by an enemy counts as not collected, so winning without returning it to the map is impossible.
- After `Won` and `Lost` the world freezes (`Tick` does nothing). `GameWorld.DeathCause` / `KillerId` tell the presentation layer how the player died (see "Death scenes" in section 5).
- Game loop (`Program.cs`): `Enter`/`Space` after a win moves to the next level; after the last one an "ALL LEVELS CLEARED" screen with a death counter is shown. `Enter`/`R` after a loss restarts the level, `R` at any time restarts the level (on the final screen — the whole campaign), `N`/`P` — debug level switching.

### 2.5 Enemies
**Movement:** same support rules as the player (run, ladder, rope, fall). Speed is lower than the player's.

**Enemy gold:**
- An enemy picks up gold when entering its cell (if not already carrying).
- The carrier drops the gold in the current cell (if it has no gold and has support/ladder/rope) after a random interval `[GoldDropMin, GoldDropMax]` ticks.
- When a carrier falls into a hole, the gold stays in the cell above the hole.

**Trap:** an enemy in a hole stands still for `EnemyTrapTicks`, then climbs up to the cell above in `EnemyClimbTicks`. If the hole closes earlier, the enemy dies.

**Death and respawn:** after `EnemyRespawnTicks` the enemy appears in a random free cell of the top row (chosen via the seeded RNG). If there are no suitable cells, rows are scanned downward to the first free one.

**Contact:** the player dies if the distance to the enemy's centre is less than `0.6 · TileUnits` in `x` and less than `0.6 · TileUnits` in `y`. The player can stand on an enemy's head (`y` difference is a whole cell) and run on it. Enemies also use each other's heads as support.

**Enemies among themselves:** they do not pass through each other horizontally on the same row.

*Implementation (M4):*
- Enemy states: `Active`, `Trapped`, `ClimbingOut`, `Dead`. Tick order: holes, player, player gold, enemies by `Id`, contact.
- Contact: Euclidean distance between `X, Y` is less than `0.6 · TileUnits` (a circle, not a rectangle). Contact is checked only for `Active`; an enemy trapped in a hole or climbing out is harmless.
- An enemy is trapped when it has landed on the boundary row in a cell with a hole in `Opening` or `Open`. It is centred on the hole column.
- Climbing out: `Y` decreases linearly by one cell over `EnemyClimbTicks`. After leaving, the hole under the feet remains support until the enemy has left its cell.
- Enemy gold stays in `GoldRemaining` (carried gold counts as not taken by the player). The drop place must be `Empty` and without gold; after dropping, the enemy does not pick it up again in the same cell until it leaves. If there is nowhere to drop, the timer stays at zero and the attempt is repeated every tick. When falling into a hole, the gold is placed in the cell above the hole if it is free.
- Respawn: a random column via the seeded RNG, then a cyclic search to the right for the first free cell of the top row. If the top row has no free cells, the enemy stays `Dead` and retries every tick (a deviation from the "downward through rows" in the plan).
- Head support: `IEntityProbe.HasExtraSupport` on `GameWorld`. A live enemy (including a trapped one) strictly below a creature in its column keeps it from falling.
- Enemy blocking: an enemy does not move horizontally if another live enemy is ahead of it no more than one cell away on the same row.

### 2.6 Enemy AI (simplified)
- Each time an enemy is fully aligned (`x` and `y` are multiples of `TileUnits`), it chooses its next direction.
- Choice: **BFS** over the cell graph from the enemy's cell to the player's cell. The first step of the shortest path is taken.
- Graph edges (from cell `(c,r)`):
  - left/right — if the destination cell is not brick/concrete and the current cell gives support (standing/ladder/rope);
  - up — if the current cell is a ladder and the cell above is passable;
  - down — if the cell below is passable (ladder, empty — the enemy will fall, rope, hole).
- Enemies do not consider each other when searching for a path.
- If there is no path, the enemy moves toward the reachable cell nearest to the player by Manhattan distance; if it is already there, it stands still.
- A 28×16 grid is 448 cells, so BFS can be called at every aligned step of every enemy without optimisation.

## 3. Time and numbers

### 3.1 Time model
- Fixed simulation step: **60 ticks/s**. Rendering runs with VSync; the simulation accumulates the delta (accumulator), with a cap on the number of steps per frame.
- Positions are **integer "units"**: `TileUnits = 48` units per cell. There is no float in the logic.
- A creature's position `(x, y)` is the top-left corner of its cell box in units. Cell = `x / TileUnits`, `y / TileUnits`. Aligned when both remainders are zero.
- **Speed must be a divisor of `TileUnits`** (1, 2, 3, 4, 6, 8, 12, 16, 24, 48). Then a creature always lands exactly on a cell boundary and accumulates no offset.

### 3.2 Initial parameters (tuned for playability; all in `GameConfig`)

| Parameter | Value | Note |
|---|---|---|
| `TileUnits` | 48 | |
| `TickRate` | 60 | |
| `PlayerRunSpeed` | 4 units/tick | 5 cells/s |
| `PlayerClimbSpeed` | 4 | |
| `PlayerRopeSpeed` | 4 | |
| `FallSpeed` | 4 | uniform |
| `EnemyRunSpeed` | 2 | 2.5 cells/s (50% of player — original game) |
| `EnemyClimbSpeed` | 2 | |
| `EnemyFallSpeed` | 2 | |
| `DigTicks` | 18 | |
| `HoleOpenTicks` | 430 | ~7.2 s (default; tunable in Settings > Gameplay) |
| `RefillTicks` | 24 | |
| `EnemyTrapTicks` | 90 | |
| `EnemyClimbTicks` | 36 | |
| `EnemyRespawnTicks` | 60 | |
| `GoldDropMin / Max` | 300 / 900 | |

These numbers are a starting point, not part of the design. They can be changed without touching the rules code.

## 4. Architecture

### 4.1 Solution structure

```
lr/
  LodeRunner.sln
  docs/DESIGN.md
  docs/AUDIO.md
  assets/levels/            *.txt, copied to the output directory
  src/
    LodeRunner.Core/        logic, no Raylib
      Levels/               TileType, LevelData, LevelParser
      Simulation/           GameWorld, Entity, Player, Enemy, HoleState, DeathCause
      Ai/                   EnemyPathfinder (BFS)
      GameConfig.cs
      InputState.cs
    LodeRunner.Game/        Raylib-cs: window, input, rendering
      Program.cs
      GameApp.cs            main loop, states
      Input/                InputReader
      Rendering/            WorldRenderer, CharacterRig, ClassicCamera, IntroCamera, DeathScene, ParticleSystem, LightingShader
      Ui/                   menus, fonts, LoadingScreen (progress bar during startup)
      Audio/                AudioManager, procedural sound and music synthesis (see docs/AUDIO.md)
  tests/
    LodeRunner.Core.Tests/  xUnit
```

Dependencies: `Game → Core`, `Tests → Core`. `Core` references neither Raylib nor `System.Numerics` for logic.

### 4.2 Core: simulation interface

```csharp
public sealed class GameWorld
{
    public GameWorld(LevelData level, GameConfig config, int seed);
    public void Tick(InputState input);          // exactly one step
    public GamePhase Phase { get; }              // Playing, Won, Lost
    public Grid Grid { get; }                    // cells + hole states
    public Player Player { get; }
    public IReadOnlyList<Enemy> Enemies { get; }
    public int GoldRemaining { get; }
    public IReadOnlyList<WorldEvent> Events { get; } // for the last tick
}
```

`WorldEvent` (gold collected, dig started, hole closed, enemy fell, player died…) is the only channel from the logic to sound/effects. The renderer reads the state but changes nothing in it.

**Order inside `Tick`:**
1. Update hole timers and turn closed holes back into brick (killing creatures inside).
2. Update the player from `InputState`.
3. Update enemies (AI, movement, gold pickup/drop, traps, respawn).
4. Player gold pickup and revealing hidden ladders.
5. Check player contact with enemies and the victory condition.

The order is fixed so that behaviour is reproducible.

### 4.3 Input
- The render layer reads the keyboard every frame and accumulates flags (`DigLeft`, `DigRight` latch until the next tick) so a short key press is not lost between ticks.
- Default controls: arrows, WASD and numpad digits (8/4/2/6) — movement, `Z` — dig left, `X` — dig right, `Esc` — pause menu. Utility keys: `R` restart, `C` camera, `F` FPS mode, `N`/`P` level (inactive if the key is bound to an action).
- Rebinding: each action has 3 key slots; assigning a key removes it from other slots. Settings (video mode and keys) are stored in `%APPDATA%\LodeRunner\settings.json` (`Game/Settings`).
- **FPS mode** (`F` key, `Game/Input/FpsController`): toggles a first-person camera. The 2-D simulation is completely untouched; FPS is a pure presentation and input-translation layer. Body heading is `Right` (+X), `Left` (−X) or `Ladder` (−Z into the screen). `W` runs forward / climbs; `S` descends a ladder or steps onto one from above; `A`/`D` initiate 180° turns (0.22 s) or 90° ladder turns (0.12 s). All movement is blocked while a turn is in progress. Mouse look is clamped to the forward hemisphere (±90° yaw, ±75° pitch). LMB digs in the direction the hero is facing; ignored while facing a ladder. FPS mode resets to off on every new level. The camera is placed at the hero's eye position (`EyeOffset (0, 0.41, 0.07) + forward * 0.13`), hero model is hidden in the main pass but still rendered in the shadow pass.
- Menus (`Game/Ui/MenuController`): main (Start, Settings, Exit), pause (Resume, Restart level, Settings, Main menu, Exit), settings → video (fullscreen / borderless window / window), controls (rebinding, reset), gameplay tuning (Enemy speed %, Hole open ticks, Refill ticks), music mixer (Bass, Pad, Wind, Bells levels). Menu navigation uses fixed keys (arrows, numpad, WASD, Enter, Esc) so a bad rebind cannot lock the menu. Navigation hints are shown at the bottom of every sub-page; the main menu shows no hint (navigation is self-evident).
- After a level loads, the simulation is frozen (`awaitingStart`): the level and enemies are shown at their start positions, and the game starts on any key press.
- Enemy speed: all three speeds (run, climb, fall) default to 2 units/tick versus 4 for the player (50%, matching the original game). Configurable in Settings > Gameplay > Enemy speed (25-100%).
- Leaving a ladder sideways: between rows `←/→` is not ignored. If there is support next to the ladder on the nearest row above or below (a brick/concrete under the neighbouring cell, a ladder, a rope), the player smoothly rides the ladder to that row and steps off. If there is no support but the neighbouring cell is free, the player steps off and falls from the current height, shifting into the neighbouring column. If there is a wall at the side, nothing happens.
- Enemies walking toward each other on one row (for example, toward the same ladder) no longer block each other: the enemy with the lower id has priority.
- The camera accounts for the window aspect ratio: the whole level fits even in narrow windows.
- The dev flag `--menu main|pause|settings|video|controls` opens the given menu page.
- Gamepad — after the MVP.

### 4.4 Level format
A text file. Lines starting with `;` are metadata (`; name: Test 1`). Then 16 lines of 28 characters (per table 2.1). Short lines are padded with spaces.

The parser checks: exactly one `&`, grid size, no unknown characters, at least one `$`. An error → a clear exception with the line number.

### 4.5 Application states
`Playing` → `LevelWon` / `LevelLost` → (on a key press) `Playing` with a new `GameWorld`. Pause is a flag that stops ticks.

### 4.6 Testing (Core)
Unit tests for: the parser; running/falling/ladders/ropes (including grabbing a rope while falling, alignment); digging and all refusal conditions; the hole life cycle; creature death in a hole; BFS (a set of mini-levels); gold pickup and drop; revealing hidden ladders; victory/defeat; determinism (two runs with the same seed and input give the same state).

## 5. Visualisation

### 5.1 World and coordinates
- Right-handed system: `+X` right, `+Y` up, `+Z` toward the camera. Grid cell `(c, r)` → centre `(c + 0.5, Rows − 1 − r + 0.5, 0)`. One cell = 1 world unit.
- Creatures are converted from units: `worldX = x / TileUnits`, `worldY = Rows − (y / TileUnits)`, and so on. Only the renderer knows these formulas.
- There is a single game plane: `z = 0`. All creatures and tiles lie in this "layer" one cell deep. The game stays logically 2D.

### 5.2 Camera
- `Camera3D`, **perspective**, fixed, pointing along `−Z`, no rotation.
- Position: `(Cols/2, Rows/2, D)`, target: `(Cols/2, Rows/2, 0)`, `up = +Y`.
- Narrow `fovy` (start — ~30°), camera far away: `D ≈ (Rows + margin) / 2 / tan(fovy/2)`. This reduces edge distortion. At 28×16 and a 1280×720 window (≈16:9) the level fits with room to spare at the sides.
- **Known consequence of perspective:** cells at the screen edge show the side faces of cubes; cells in the centre show only front faces. Grid readability drops slightly. If this hurts play, in Raylib it is enough to switch `Camera3D.Projection` to `Orthographic` — the logic and the rest of the renderer do not change. The switch lives in `CameraRig`.
- The window size is fixed at start (1280×720); resizing — after the MVP.
- The camera is hidden behind the `ICameraMode` interface. The MVP has one mode, `Classic`. For other modes see section 9.

*Implementation (M6):* the `C` key switches between perspective and orthographic on the fly. `ClassicCamera` defaults: `fovy = 30°`, `margin = 2`. For debugging: `--level N` (start level), `--ortho`, `--nolight`, `--shot name.png` (screenshot after 90 frames, then exit; the file is saved next to the exe), `--closeup` (camera next to the hero and no start banner, for inspecting the character model).

**Effects (M6):** `ParticleSystem` — purely visual particles driven by `WorldEvent`s (gold, digging, hole opening and closing, trap, enemy death and respawn, victory). It has its own RNG and uses frame time; it does not touch the core. Blood is a separate pool of small red spheres (`Blood(...)`).

**Level intro and fades:** on a *new* level (game start, continue, level select, next level, N/P) the world stays paused. `IntroCamera` flies the camera from the hero's eyes (FOV 78°) back and to the side while turning 90° into the `ClassicCamera` pose (FOV 30°) over `IntroDuration` = 5 s; the hero model is hidden while the camera is inside his head. The "LEVEL N / Press any key to start" plate appears only after the shot ends. The first key press during the shot skips it, the next one starts the level. Retry after death and `R` skip the intro and the fades. Fades (`Program.cs`): level load starts black (`BlackHoldTime` 0.5 s) and fades back over `FadeFromBlackTime` 1 s; after the "LEVEL COMPLETE" plate a key press fades to black over `FadeToBlackTime` 0.6 s, then the next level loads. Input is ignored during the fades.

**Death scenes (`DeathScene`):** the core reports *why* the hero died through `GameWorld.DeathCause` (`Crushed`, `Caught`, `GuardFellOnHero`, `HeroFellOnGuard`) and `KillerId`; this is informational only and does not affect the simulation. The renderer turns it into pose modifiers (`CharacterRig.Fx`: `Jitter`, `Flail`, `Crush`, `Offset`) and blood sprays. While the hero stands in a refilling hole (before he dies) the simulation runs at 1/3 speed, his body jerks and bleeds; a guard in a refilling hole does the same. After the death the world is frozen, but animation, blood and camera continue in 1/3 speed slow motion, and the camera pushes in on the hero. The "You died" plate and the retry key become available `DeathScene.ReadyAfter` = 1.9 s after the death. Animation uses a separate `gameClock` that slows down with the scene. Dev flags: `--intro-t 0..1` freezes the intro at a fraction, `--audio-diag` writes `audio-diag.log` with audio stream timing.

### 5.3 Models (Raylib primitives only)

| Object | Representation |
|---|---|
| Brick | coloured cube + `DrawCubeWires` outline; in the hole state the cube shrinks in `y` (dig/close frames) or is not drawn |
| Concrete | darker/colder cube with a frame |
| Ladder | two thin rail bars + 4–5 rungs, ~0.3 deep |
| Rope | thin horizontal cylinder at the top of the cell |
| Gold | small cube/ingot, slowly rotating (purely visual, does not affect the logic) |
| Player | the hero (`CharacterRig.Hero.cs`): clay-style body in a sand-coloured sleeveless tunic with a ragged, asymmetric hem (a cloth surface of revolution, like the monk's cloak), bare arms with wrist wraps, a diagonal leather strap with a satchel, a second belt, a shoulder scarf, thigh straps, tall laced boots and shoulder-length brown hair; faces left/right |
| Enemy | a hooded monk: the cloak is a smooth surface of revolution (rings of vertices with folds, swaying hem, wind billow when running, flare when falling) emitted straight into the Rlgl batch with real normals; the hood is a void with slanted glowing red eyes (an additive unlit glow pass) and a row of bone teeth; pale bony hands; carried gold is clutched at the chest (`CharacterRig.Monk.cs`) |

Animations (running, climbing, digging) are procedural, driven by the character state and the tick counter. There are no sprites and no skeletal animation.

### 5.4 Lighting
MVP: no lighting — flat colours plus outline lines (`DrawCubeWires`) so the faces read clearly. A basic lighting shader (one directional lamp, Lambert) is added as a separate milestone in the polish phase: it gives a noticeably more "3D" look. This is the only place where shaders are needed.

*Implementation (M6):* `LightingShader` — GLSL 330, one directional lamp plus ambient. Raylib immediate-mode primitives have no normals, so the fragment shader reconstructs the face normal from `dFdx/dFdy` of the world position (flat shading). Lines (`DrawCubeWires`) have no area and stay unlit. Front faces (normal `+Z`) get a multiplier of exactly 1.0, so the source colours are preserved; top faces are lighter, bottom and lamp-facing-away faces are darker. Parameters: `LightDirection = (-0.5, 0.75, 0.45)`, `Ambient = 0.62`, `Diffuse = 0.85`. If the shader fails to build, `IsAvailable = false` and the game draws without lighting. Particles are drawn outside the shader, so they are always bright. The `L` key toggles lighting.

### 5.5 HUD
Raylib text only: level name, `Gold: N`, the messages "Level complete" / "You died — press R". Replaced by a proper UI later.

### 5.6 Sound
There are no audio files in the project: all effects and the background music are synthesised by code in `LodeRunner.Game/Audio`. `Core` knows nothing about audio; it only emits simulation events, which `AudioManager` turns into sounds.

- Effects are generated as 16-bit stereo PCM WAV in memory and loaded through `Raylib.LoadWaveFromMemory`. All variants are rendered in parallel (`Parallel.For`) at startup; uploading to Raylib happens sequentially on the main thread.
- Each event has 3–10 pre-rendered variants (different pitch, formants, decay, timing). On playback an idle variant is chosen: repeats never sound identical, and reverb tails are not cut off.
- Techniques: modal synthesis (stone, metal, bells), formant voice synthesis (glottal pulse + 3 band-pass filters), a choir with vibrato and hall reverb, a real-time 5-layer ambient drone (`DarkAmbientSynth`).
- `Fall`, `Death` and `EnemyDie` are deliberately quiet and muffled so that frequent repeats do not cause listening fatigue.

Details are in `docs/AUDIO.md`.

## 6. Implementation plan (milestones)

| # | Milestone | Result |
|---|---|---|
| M0 | Skeleton | `.sln`, three projects, Raylib window, camera, static cubes |
| M1 | Levels | parser, loading from file, rendering of all cell types |
| M2 | Player | running, falling, ladders, ropes + tests |
| M3 | Gold and holes | pickup, digging, hole life cycle, hidden ladders |
| M4 | Enemies | movement, BFS, traps, enemy gold, respawn (**done**) |
| M5 | Game loop | win/loss, restart, HUD, 3 test levels (**done**) |
| M6 | Polish | lighting, animations, camera parameter tuning, parameter tuning (**done**) |

MVP exit criterion: all three test levels are completable from start to finish; `Core` tests are green; the simulation is deterministic.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Perspective reduces grid readability | Narrow FOV + quick switch to Orthographic (5.2) |
| Looks flat without light | Outline lines in the MVP, shader in M6 |
| Subtle alignment bugs on ladders and ropes | Integer units, speeds that divide `TileUnits`, many unit tests |
| Behaviour "not like the original" | Parameters live in `GameConfig`; the AI is isolated behind an interface and can be replaced with the exact original algorithm |
| Raylib-cs API changes between versions | Pin the package version in the `.csproj`; Raylib is used only in the `Game` project |

| Photorealism in Raylib is limited: PBR, shadows and post-processing must be written by hand | The renderer is isolated in `LodeRunner.Game`; the engine is reconsidered in milestone P5 |
| Human characters are a content question (motion capture, scans, licences), not a code one | A separate content task after the MVP |

## 8. Decisions made
1. **Level exit.** As in the original: after all gold is collected, hidden ladders to the top row appear.
2. **Minor digging rules** (no digging from a rope, onto gold, and so on) — as in typical clones, section 2.2.
3. **Levels.** The MVP has three custom test levels.
4. **Render engine.** Raylib-cs for now, to be reconsidered in milestone P5.

## 9. Vision and groundwork for the future (outside the MVP, no commitments)

### 9.1 Idea
After the MVP, build up the beauty step by step: photorealistic graphics, real people as the hero and enemies, switching the camera between the classic view and a first-person view. The FPS mode is postponed for a long time. Its controls, mouse look and hiding the player's body are not being designed now.

### 9.2 What we set up already
- `Core` does not depend on rendering, so the renderer can be replaced entirely.
- For each creature, positions of the previous and current tick are stored. The renderer interpolates them, and any camera moves smoothly.
- The camera is hidden behind `ICameraMode`. The MVP has one mode, `Classic`.
- The appearance of objects is hidden behind the `ITileVisual` and `ICharacterVisual` interfaces. The first implementation is primitives, later models loaded from files.
- The simulation state (running, climbing, hanging, digging, falling, in a hole, dead) unambiguously determines the animation clip.
- The back wall, floor and ceiling are a separate visual layer that the logic does not know about.

### 9.3 Milestones after M6 (tentative)
| # | Milestone | Result |
|---|---|---|
| P1 | Interpolation and FPS | smooth movement, first-person mode |
| P2 | Visual themes | loading models and textures from files |
| P3 | Skeletal characters | binding animations to the simulation state |
| P4 | Lighting | PBR, shadows, post-processing |
| P5 | Engine review | assess whether Raylib is enough, migrate the renderer if necessary |
