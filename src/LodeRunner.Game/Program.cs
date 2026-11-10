using LodeRunner.Core;
using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using LodeRunner.Game.Audio;
using LodeRunner.Game.Input;
using LodeRunner.Game.Rendering;
using LodeRunner.Game.Settings;
using LodeRunner.Game.Ui;
using Raylib_cs;

const int MaxStepsPerFrame = 5;
const float IntroDuration = 5f; // seconds of the level intro camera shot
const float FadeToBlackTime = 0.6f; // leaving a level: screen goes dark
const float BlackHoldTime = 0.5f;   // new level loaded: stays dark for a moment
const float FadeFromBlackTime = 1.0f; // new level: screen comes back while the camera starts to move

string levelsDir = Path.Combine(AppContext.BaseDirectory, "assets", "levels");
var levelFiles = Directory.Exists(levelsDir)
    ? Directory.GetFiles(levelsDir, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray()
    : [];

if (levelFiles.Length == 0)
{
    Console.Error.WriteLine($"No level files found in {levelsDir}");
    return 1;
}

var levels = new List<LevelData>();
foreach (var file in levelFiles)
{
    try
    {
        levels.Add(LevelParser.Load(file));
    }
    catch (LevelParseException ex)
    {
        Console.Error.WriteLine($"{Path.GetFileName(file)}: {ex.Message}");
        return 1;
    }
}

var settings = GameSettings.Load();
var config = settings.ToGameConfig();
double tickDuration = TickDurationFor(settings.Speed);

Raylib.SetTraceLogLevel(TraceLogLevel.None);
Raylib.SetConfigFlags(ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
Raylib.InitWindow(VideoManager.WindowedWidth, VideoManager.WindowedHeight, "Lode Runner");
string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icon", "lode-runner.png");
Image windowIcon = default;
bool hasIcon = File.Exists(iconPath);
if (hasIcon)
{
    windowIcon = Raylib.LoadImage(iconPath);
    Raylib.SetWindowIcon(windowIcon);
}
Raylib.SetWindowMinSize(640, 360);
Raylib.SetExitKey(KeyboardKey.Null); // Esc opens the pause menu; quitting goes through the menu
VideoManager.Apply(settings.Video);
UiFont.Load(Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "PressStart2P-Regular.ttf"));

// Loading screen: window content is drawn right away instead of a blank window while objects are created.
LoadingScreen.Show(0.02f, "Creating game objects...");
using var audio = new AudioManager(LoadingScreen.Show);
audio.Volume = settings.Volume / 100f;
audio.MusicVolume = settings.MusicVolume / 100f;
audio.SetMixerLevels(settings.MusicBass / 100f, settings.MusicPad / 100f, settings.MusicWind / 100f, settings.MusicBells / 100f);
LoadingScreen.Show(0.95f, "Compiling shaders...");
var lighting = new LightingShader();
var renderer = new WorldRenderer(lighting);
LoadingScreen.Show(0.98f, "Creating shadow map...");
using var shadowMap = new ShadowMap();
lighting.HighDetail = settings.Graphics == GraphicsQuality.High;
var particles = new ParticleSystem();
var death = new DeathScene();
renderer.Death = death;
var scores = ScoreStore.Load();
var menu = new MenuController(settings, levels.Count, scores) { Sound = audio.Play };
bool orthographic = args.Contains("--ortho");
bool closeup = args.Contains("--closeup");
AudioDiag.Enabled = args.Contains("--audio-diag"); // dev aid: writes audio-diag.log (stream feed timing, GC)
int shotIdx = Array.IndexOf(args, "--shot");
string? shotPath = shotIdx >= 0 && shotIdx + 1 < args.Length ? Path.GetFileName(args[shotIdx + 1]) : null;
int frameCount = 0;
int startLevel = 0;
int levelIdx = Array.IndexOf(args, "--level");
bool levelGiven = false;
if (levelIdx >= 0 && levelIdx + 1 < args.Length && int.TryParse(args[levelIdx + 1], out int lv))
{
    startLevel = Math.Clamp(lv - 1, 0, levels.Count - 1);
    levelGiven = true;
}
Color background = new(16, 18, 28, 255);

var campaign = new Campaign(levels.Count, startLevel);
bool runActive = false;   // a run has started and is not yet recorded in the score table
int lastRank = -1;        // rank of the run recorded last, shown on the end screens
bool awaitingStart = !closeup; // level is shown but frozen until a key is pressed (dev --closeup skips it)
float introT = closeup ? IntroDuration : 0f; // seconds into the level intro shot (eye view -> gameplay camera)
int introFixedIdx = Array.IndexOf(args, "--intro-t"); // dev aid: freeze the intro at a fraction 0..1 (for --shot)
float? introFixed = introFixedIdx >= 0 && introFixedIdx + 1 < args.Length
    && float.TryParse(args[introFixedIdx + 1], System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out float fx)
    ? Math.Clamp(fx, 0f, 1f) * IntroDuration
    : null;
bool useFades = shotPath is null && introFixed is null && !closeup; // dev screenshots must not catch a fade
float fadeAlpha = 0f;        // 0 = scene visible, 1 = fully black
float blackHold = 0f;        // seconds left to stay black after a level was loaded
Action? afterFadeToBlack = null; // runs once the screen is black (loads the next level)
bool quit = false;
GameWorld world = NewWorld(campaign.LevelIndex);
double accumulator = 0;
float alpha = 0;
bool digLeftLatch = false;
bool digRightLatch = false;
float shake = 0f; // remaining camera shake time in seconds
float gameClock = 0f; // animation clock; runs slower during death slow motion
const float ShakeDuration = 0.45f;
float idleSway = 0f; // 0..1 blend of the slow camera sway shown while the game is paused or between rounds
float swayClock = 0f;
bool fpsMode = false;         // FPS mode active (toggled by F key)
var fpsController = new FpsController();
bool cursorHidden = false;    // tracks Raylib cursor capture state

// Startup page: main menu, unless a dev flag asks to go straight into a level.
int menuIdx = Array.IndexOf(args, "--menu");
if (menuIdx >= 0 && menuIdx + 1 < args.Length)
{
    menu.Open(args[menuIdx + 1].ToLowerInvariant() switch
    {
        "pause" => MenuPage.Pause,
        "settings" => MenuPage.Settings,
        "video" => MenuPage.Video,
        "controls" => MenuPage.Controls,
        "scores" => MenuPage.HighScores,
        _ => MenuPage.Main,
    });
}
else if (!levelGiven && shotPath is null)
{
    menu.Open(MenuPage.Main);
}

while (!quit && !Raylib.WindowShouldClose())
{
    float frameTime = Raylib.GetFrameTime();
    var keys = settings.Keys;
    PadInput.Update(frameTime);

    if (menu.Active)
    {
        var cmd = menu.Update();
        switch (cmd)
        {
            case MenuCommand.VolumeChanged:
                audio.Volume = settings.Volume / 100f;
                break;
            case MenuCommand.MusicVolumeChanged:
                audio.MusicVolume = settings.MusicVolume / 100f;
                break;
            case MenuCommand.EnemyAiChanged:
                world.EnemyAi = settings.EnemyAi;
                break;
            case MenuCommand.GraphicsChanged:
                lighting.HighDetail = settings.Graphics == GraphicsQuality.High;
                break;
            case MenuCommand.GameSpeedChanged:
                tickDuration = TickDurationFor(settings.Speed);
                accumulator = 0;
                break;
            case MenuCommand.GameplayChanged:
                config = settings.ToGameConfig();
                break;
            case MenuCommand.MusicMixerChanged:
                audio.SetMixerLevels(settings.MusicBass / 100f, settings.MusicPad / 100f, settings.MusicWind / 100f, settings.MusicBells / 100f);
                break;
            case MenuCommand.NewGame:
            case MenuCommand.Continue:
            case MenuCommand.StartLevel:
                RecordRun();
                campaign.Restart(cmd switch
                {
                    MenuCommand.Continue => settings.MaxUnlockedLevel,
                    MenuCommand.StartLevel => menu.SelectedLevel,
                    _ => 0,
                });
                world = NewWorld(campaign.LevelIndex);
                menu.Close();
                DrainKeys();
                break;
            case MenuCommand.Resume:
                menu.Close();
                DrainKeys();
                break;
            case MenuCommand.RestartLevel:
                RestartLevel();
                menu.Close();
                DrainKeys();
                break;
            case MenuCommand.ToMainMenu:
                RecordRun();
                menu.Open(MenuPage.Main);
                break;
            case MenuCommand.Exit:
                quit = true;
                break;
        }
    }
    else
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || PadInput.StartPressed)
        {
            audio.StopFall(); // a fall sound must not keep playing under the menu
            // Nothing left to resume after the run ended: go straight to the main menu.
            bool runOver = campaign.IsGameOver || campaign.IsComplete;
            if (!runOver) audio.Play(Sfx.Pause);
            menu.Open(runOver ? MenuPage.Main : MenuPage.Pause);
        }
        else
        {
            HandleGameInput();
        }
    }

    // Fixed-step simulation; leftover time becomes the interpolation factor.
    bool running = !menu.Active && !awaitingStart && !campaign.IsComplete && !campaign.IsGameOver;
    // FPS controller: update once per rendered frame before the sim ticks so movement is ready.
    // fpsActive here is the same condition used later for the camera block (intro not yet evaluated,
    // but fpsMode is enough — intro forces fpsActive=false in the camera block anyway).
    bool fpsRunning = fpsMode && running;
    FpsOutput fpsOut = default;
    if (fpsRunning)
    {
        var md = Raylib.GetMouseDelta();
        int playerGridCol = (world.Player.X + world.Grid.TileUnits / 2) / world.Grid.TileUnits;
        int playerGridRow = (world.Player.Y + world.Grid.TileUnits / 2) / world.Grid.TileUnits;
        fpsOut = fpsController.Update(
            dt: frameTime,
            mouseDeltaX: md.X,
            mouseDeltaY: md.Y,
            wDown: Raylib.IsKeyDown(KeyboardKey.W),
            sDown: Raylib.IsKeyDown(KeyboardKey.S),
            aPressed: Raylib.IsKeyPressed(KeyboardKey.A),
            dPressed: Raylib.IsKeyPressed(KeyboardKey.D),
            lmbPressed: Raylib.IsMouseButtonPressed(MouseButton.Left),
            playerFacing: world.Player.Facing,
            playerFalling: world.Player.Falling,
            playerPose: world.Player.Pose,
            playerGridCol: playerGridCol,
            playerGridRow: playerGridRow,
            grid: world.Grid);
    }
    if (running)
    {
        accumulator += Math.Min(frameTime, tickDuration * MaxStepsPerFrame) * death.SimScale; // slow motion while the hero is bricked in
        var held = fpsRunning ? fpsOut.Movement : ReadMovement(keys);
        int steps = 0;
        while (accumulator >= tickDuration && steps < MaxStepsPerFrame)
        {
            bool dl = fpsRunning ? fpsOut.DigLeft  : digLeftLatch;
            bool dr = fpsRunning ? fpsOut.DigRight : digRightLatch;
            world.Tick(held with { DigLeft = dl, DigRight = dr });
            campaign.Apply(world.Events);
            audio.OnTick(world, campaign.IsGameOver);
            foreach (var ev in world.Events)
                if (ev.Kind == WorldEventKind.LevelWon) settings.Unlock(campaign.LevelIndex + 1, levels.Count);

            foreach (var ev in world.Events)
            {
                particles.OnEvent(ev, world.Grid.Rows);
                if (ev.Kind == WorldEventKind.LaddersRevealed) particles.RevealSparkle(world);
                if (ev.Kind == WorldEventKind.PlayerDied)
                {
                    shake = ShakeDuration;
                    death.Start(world, particles);
                }
            }
            digLeftLatch = digRightLatch = false;
            accumulator -= tickDuration;
            steps++;
        }
        alpha = (float)(accumulator / tickDuration);
    }
    else if (!menu.Active)
    {
        alpha = 0;
    }
    if (world.Phase != GamePhase.Playing) alpha = 1f; // the world is frozen: show the final positions, no re-interpolation

    // Death scene (slow-motion blood, convulsions) and particles keep running while the simulation is frozen.
    if (!menu.Active)
    {
        death.Update(world, particles, Math.Min(frameTime, 0.05f));
        if (death.HeroBricking && campaign.Lives > 1) audio.OnHeroBricking(); // scream while the bricks close in, not after
        particles.Update(frameTime * death.VisualScale);
    }

    // Game over happens inside a tick, but a full clear is set by the Enter press after the last win,
    // when the simulation is no longer running, so the check lives outside the running block.
    if (campaign.IsGameOver || campaign.IsComplete) RecordRun();

    // Level intro shot advances only while the level is waiting for its start key and no menu covers it.
    // The step is capped so a long frame (e.g. right after loading) cannot swallow the shot.
    // Level transition fades: fade to black -> (load) -> hold black -> fade back in. Frozen under menus.
    if (!menu.Active)
    {
        float dt = Math.Min(frameTime, 0.05f);
        if (afterFadeToBlack is not null)
        {
            fadeAlpha = Math.Min(1f, fadeAlpha + dt / FadeToBlackTime);
            if (fadeAlpha >= 1f)
            {
                var load = afterFadeToBlack;
                afterFadeToBlack = null;
                load(); // NewWorld() re-arms the black hold
            }
        }
        else if (blackHold > 0f) blackHold -= dt;
        else if (fadeAlpha > 0f) fadeAlpha = Math.Max(0f, fadeAlpha - dt / FadeFromBlackTime);
    }

    bool introWaiting = awaitingStart && !menu.Active && !campaign.IsGameOver && !campaign.IsComplete
        && world.Phase == GamePhase.Playing && blackHold <= 0f && afterFadeToBlack is null;
    if (introFixed is { } frozen) introT = frozen;
    else if (introWaiting && introT < IntroDuration) introT = Math.Min(IntroDuration, introT + Math.Min(frameTime, 0.05f));
    if (orthographic) introT = IntroDuration; // the shot is a perspective move

    gameClock += frameTime * death.VisualScale;
    int sw = Raylib.GetScreenWidth();
    int sh = Raylib.GetScreenHeight();
    float ui = sh / 720f;
    float aspect = sh > 0 ? (float)sw / sh : 16f / 9f;
    ICameraMode cameraMode = new ClassicCamera(world.Grid.Cols, world.Grid.Rows, orthographic: orthographic, aspect: aspect);
    var cam = cameraMode.Camera;
    var heroPos = GridCoords.EntityCenter(world.Player.PrevX, world.Player.PrevY, world.Player.X, world.Player.Y, alpha,
        world.Grid.TileUnits, world.Grid.Rows);
    bool introActive = awaitingStart && introT < IntroDuration && !closeup && !orthographic
        && !campaign.IsGameOver && !campaign.IsComplete && world.Phase == GamePhase.Playing
        && (introWaiting || menu.Active && menu.Page != MenuPage.Main);
    float introProgress = introActive ? introT / IntroDuration : 1f;
    renderer.HidePlayer = false;
    if (introActive)
    {
        cam = IntroCamera.Evaluate(cam, heroPos, world.Player.Facing == Facing.Right ? 1f : -1f, introProgress);
        renderer.HidePlayer = System.Numerics.Vector3.Distance(cam.Position, IntroCamera.HeadCenter(heroPos)) < IntroCamera.HideHeroRadius;
    }
    // FPS camera overrides the classic camera (but not the intro or closeup dev camera).
    bool fpsActive = fpsMode && !introActive && !closeup
                     && !campaign.IsGameOver && !campaign.IsComplete
                     && world.Phase == GamePhase.Playing;
    if (fpsActive)
    {
        // fpsOut was already updated in the fpsRunning block above (before the sim tick).
        // If the game just became non-running (e.g. level won overlay), fpsOut is still
        // from last frame which is fine — camera stays put.
        cam = FpsCamera.Evaluate(heroPos, fpsOut.CameraYaw, fpsOut.CameraPitch);
        renderer.HidePlayer = true;
    }
    if (closeup)
    {
        // Dev aid: camera right next to the hero (and a slight 3/4 angle) for inspecting the character model.
        var hp = GridCoords.EntityCenter(world.Player.PrevX, world.Player.PrevY, world.Player.X, world.Player.Y, alpha,
            world.Grid.TileUnits, world.Grid.Rows);
        cam.Target = hp + new System.Numerics.Vector3(0f, 0.05f, 0f);
        cam.Position = hp + new System.Numerics.Vector3(1.1f, 0.15f, 4.2f);
        cam.FovY = 30f;
        cam.Projection = CameraProjection.Perspective;
    }
    if (!introActive && !fpsActive) cam = death.ApplyCamera(cam, heroPos); // slow push-in on the hero while he dies
    if (!fpsActive && shake > 0f)
    {
        float k = shake / ShakeDuration;
        float amp = 0.18f * k * k;
        float t = (float)Raylib.GetTime();
        var off = new System.Numerics.Vector3(MathF.Sin(t * 83f) * amp, MathF.Cos(t * 97f) * amp, 0f);
        cam.Position += off;
        cam.Target += off;
        shake = Math.Max(0f, shake - frameTime);
    }

    // Slow, smooth camera sway while the game is held: pause menu, "press any key", death, level complete.
    bool gameHeld = menu.Active || awaitingStart || campaign.IsComplete || campaign.IsGameOver
        || world.Phase is GamePhase.Lost or GamePhase.Won;
    audio.UpdateMusic(world, gameHeld, campaign.LevelIndex);
    idleSway = Math.Clamp(idleSway + (gameHeld ? frameTime : -frameTime) / 1.2f, 0f, 1f);
    if (idleSway > 0f && !fpsActive)
    {
        swayClock += frameTime;
        float w = idleSway * idleSway * (3f - 2f * idleSway);
        if (introActive) w *= IntroCamera.Ease(introProgress); // no swaying of the camera while it sits in the hero's eyes
        w *= 1f - death.Zoom; // steady camera during the death close-up
        var sway = new System.Numerics.Vector3(
            MathF.Sin(swayClock * 0.55f) * 1.0f,
            MathF.Sin(swayClock * 0.40f + 1.3f) * 0.5f,
            0f) * w;
        cam.Position += sway;
        cam.Target += sway * 0.25f;
    }

    bool useShadows = lighting.IsAvailable && shadowMap.IsValid && settings.Graphics == GraphicsQuality.High;
    // FPS cursor: capture mouse when FPS is active and playing; release for menus/death/end screens.
    bool wantCursorHidden = fpsMode && !menu.Active && !awaitingStart
                            && !campaign.IsGameOver && !campaign.IsComplete
                            && world.Phase == GamePhase.Playing;
    if (wantCursorHidden && !cursorHidden) { Raylib.DisableCursor(); cursorHidden = true; }
    if (!wantCursorHidden && cursorHidden) { Raylib.EnableCursor();  cursorHidden = false; }

    if (useShadows)
    {
        shadowMap.Begin(lighting.LightDirection, world.Grid.Cols, world.Grid.Rows);
        renderer.Draw(world, gameClock, alpha, shadowPass: true);
        shadowMap.End();
    }
    lighting.UseShadows(useShadows ? shadowMap : null);

    Raylib.BeginDrawing();
    Raylib.ClearBackground(background);
    Raylib.DrawRectangleGradientV(0, 0, sw, sh, new Color(30, 36, 66, 255), new Color(8, 9, 16, 255));

    Raylib.BeginMode3D(cam);
    lighting.Begin(cam.Position);
    renderer.Draw(world, gameClock, alpha);
    lighting.End();
    WorldRenderer.DrawEffects(world, gameClock, alpha);
    renderer.DrawEyeGlows(gameClock);
    particles.Draw();
    Raylib.EndMode3D();
    DrawVignette(sw, sh);

    if (!menu.Active || menu.Page != MenuPage.Main)
        DrawHud(sw, sh, ui);

    if (menu.Active)
    {
        if (menu.Page != MenuPage.Main) DrawFade(sw, sh);
        menu.Draw(gameBehind: menu.Page != MenuPage.Main);
    }
    else
    {
        DrawOverlays(sw, sh, ui);
        DrawFade(sw, sh);
    }

    Raylib.EndDrawing();

    // Dev aid: `--shot <path>` saves a screenshot after a short warm-up and exits.
    if (shotPath is not null && ++frameCount == 90)
    {
        Raylib.TakeScreenshot(shotPath);
        break;
    }
}

RecordRun(); // quitting mid-run still counts the run
lighting.Dispose();
SoftSprite.Unload();
UiFont.Unload();
if (hasIcon) Raylib.UnloadImage(windowIcon);
Raylib.CloseWindow();
return 0;

// ---------------------------------------------------------------------------------------------

// Writes the current run to the score table once, when it ends (game over, cleared) or is abandoned.
void RecordRun()
{
    if (!runActive) return;
    runActive = false;
    lastRank = scores.Add(new ScoreEntry(campaign.Score, campaign.LevelIndex + 1, campaign.Deaths,
        campaign.IsComplete, DateTime.Now));
    ScoreStore.Save(scores);
    menu.HighlightRank = lastRank;
}

GameWorld NewWorld(int index, bool intro = true)
{
    accumulator = 0;
    alpha = 0;
    awaitingStart = !closeup;
    introT = intro && !closeup ? 0f : IntroDuration; // retries and restarts skip the fly-in
    // A freshly loaded level starts in darkness and fades in; retries and restarts cut straight back in.
    afterFadeToBlack = null;
    fadeAlpha = intro && useFades ? 1f : 0f;
    blackHold = intro && useFades ? BlackHoldTime : 0f;
    particles.Clear();
    shake = 0f;
    death.Reset(snapCamera: intro); // a retry lets the camera ease back out; a fresh level starts clean
    audio.Reset();
    digLeftLatch = digRightLatch = false;
    fpsMode = false; // FPS mode resets on every new level
    return new GameWorld(levels[index], config with { EnemyAi = settings.EnemyAi }, seed: 1);
}

// Restart rules: score earned on the level is always dropped so it cannot be farmed.
//  - mid-level: costs a life
//  - after a win: free, the level bonus and bonus life are taken back
//  - after a death: retry; the life stays lost, score of the failed attempt is dropped
//  - before the level started: nothing to undo
void RestartLevel()
{
    switch (world.Phase)
    {
        case GamePhase.Won:
            campaign.RevertScore();
            campaign.RevertLives();
            break;
        case GamePhase.Playing when !awaitingStart:
            campaign.RevertScore();
            campaign.LoseLife();
            campaign.MarkLevelStart();
            break;
        case GamePhase.Lost:
            campaign.RevertScore();
            campaign.MarkLevelStart();
            break;
    }
    world = NewWorld(campaign.LevelIndex, intro: false);
}

double TickDurationFor(GameSpeed speed) => 1.0 / (config.TickRate * GameSpeedInfo.Multiplier(speed));

void DrainKeys()
{
    while (Raylib.GetKeyPressed() != 0) { }
    while (Raylib.GetCharPressed() != 0) { }
}

void HandleGameInput()
{
    var keys = settings.Keys;

    // Input is ignored while the screen is fading out or the next level is still dark.
    if (afterFadeToBlack is not null || blackHold > 0f)
    {
        DrainKeys();
        return;
    }

    // Dev / convenience keys. A key the player bound to an action never acts as one of these.
    bool dev(KeyboardKey k) => Raylib.IsKeyPressed(k) && !keys.IsBound(k);

    // Level skipping is a dev aid for a live run; once the run is over only Enter/R/Esc apply.
    bool runOver = campaign.IsGameOver || campaign.IsComplete;
    if (!runOver && (dev(KeyboardKey.PageDown) || dev(KeyboardKey.N)))
    {
        if (campaign.LevelIndex + 1 <= settings.MaxUnlockedLevel)
        {
            campaign.GoTo(campaign.LevelIndex + 1);
            world = NewWorld(campaign.LevelIndex);
        }
    }
    if (!runOver && (dev(KeyboardKey.PageUp) || dev(KeyboardKey.P)))
    {
        if (campaign.LevelIndex > 0)
        {
            campaign.GoTo(campaign.LevelIndex - 1);
            world = NewWorld(campaign.LevelIndex);
        }
    }
    if (dev(KeyboardKey.R))
    {
        if (campaign.IsGameOver || campaign.IsComplete)
        {
            campaign.Restart(0);
            world = NewWorld(campaign.LevelIndex);
        }
        else
        {
            RestartLevel();
        }
    }
    if (dev(KeyboardKey.C)) orthographic = !orthographic;
    // F toggles FPS mode. Sync the controller to the current sim facing when activating.
    if (dev(KeyboardKey.F))
    {
        fpsMode = !fpsMode;
        if (fpsMode) fpsController.Reset(world.Player.Facing);
    }

    bool confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) ||
                   Raylib.IsKeyPressed(KeyboardKey.Space) || PadInput.ConfirmPressed;
    if (confirm)
    {
        if (!death.Ready) return; // let the death scene play
        if (campaign.IsGameOver || campaign.IsComplete)
        {
            campaign.Restart(0);
            world = NewWorld(campaign.LevelIndex);
            DrainKeys();
            return;
        }
        if (world.Phase == GamePhase.Lost)
        {
            RestartLevel();
            DrainKeys();
            return;
        }
        if (world.Phase == GamePhase.Won)
        {
            bool lastLevel = campaign.LevelIndex + 1 >= levels.Count;
            if (lastLevel || !useFades)
            {
                if (campaign.AdvanceLevel()) world = NewWorld(campaign.LevelIndex);
            }
            else
            {
                // Fade to black first; the next level is loaded once the screen is dark.
                afterFadeToBlack = () =>
                {
                    if (campaign.AdvanceLevel()) world = NewWorld(campaign.LevelIndex);
                };
            }
            DrainKeys();
            return;
        }
    }

    // Level is loaded but frozen: any key starts it.
    if (awaitingStart && !campaign.IsGameOver && !campaign.IsComplete && world.Phase == GamePhase.Playing)
    {
        bool any = false;
        int k;
        while ((k = Raylib.GetKeyPressed()) != 0)
        {
            var key = (KeyboardKey)k;
            if (key != KeyboardKey.Escape && !(IsDevKeyRaw(key) && !keys.IsBound(key))) any = true;
        }
        if (any || PadInput.AnyPressed)
        {
            if (introT < IntroDuration)
            {
                introT = IntroDuration; // first press only skips the fly-in; the next one starts the level
                return;
            }
            awaitingStart = false;
            runActive = true;
            lastRank = -1;
            accumulator = 0;
        }
        return;
    }

    // Dig keys are one-shot requests: latch a press until a tick consumes it, so a short tap
    // between two ticks is never lost. Movement keys are held state.
    digLeftLatch |= keys.WasPressed(GameAction.DigLeft);
    digRightLatch |= keys.WasPressed(GameAction.DigRight);
}

static bool IsDevKeyRaw(KeyboardKey k) =>
    k is KeyboardKey.C or KeyboardKey.F or KeyboardKey.N or KeyboardKey.P or KeyboardKey.R
        or KeyboardKey.PageUp or KeyboardKey.PageDown;

void DrawFade(int sw, int sh)
{
    if (fadeAlpha <= 0f) return;
    float a = fadeAlpha * fadeAlpha * (3f - 2f * fadeAlpha); // smoothstep
    Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, (int)(a * 255)));
}

void DrawVignette(int sw, int sh)
{
    int edgeV = (int)(sh * 0.16f), edgeH = (int)(sw * 0.10f);
    var dark = new Color(0, 0, 0, 120);
    var clear = new Color(0, 0, 0, 0);
    Raylib.DrawRectangleGradientV(0, 0, sw, edgeV, dark, clear);
    Raylib.DrawRectangleGradientV(0, sh - edgeV, sw, edgeV, clear, dark);
    Raylib.DrawRectangleGradientH(0, 0, edgeH, sh, dark, clear);
    Raylib.DrawRectangleGradientH(sw - edgeH, 0, edgeH, sh, clear, dark);
}

void DrawHeart(int cx, int cy, int r, Color color)
{
    Raylib.DrawCircle(cx - r / 2, cy - r / 3, r * 0.55f, color);
    Raylib.DrawCircle(cx + r / 2, cy - r / 3, r * 0.55f, color);
    Raylib.DrawTriangle(new System.Numerics.Vector2(cx - r * 1.03f, cy - r / 6f),
        new System.Numerics.Vector2(cx, cy + r * 1.05f), new System.Numerics.Vector2(cx + r * 1.03f, cy - r / 6f), color);
}

void DrawHud(int sw, int sh, float ui)
{
    int size = Math.Max(10, (int)(20 * ui)), small = Math.Max(9, (int)(16 * ui)), pad = (int)(10 * ui);
    int barH = (int)(64 * ui);

    // FPS mode: draw a crosshair instead of the full HUD bar while actively playing.
    if (fpsMode && !menu.Active && !awaitingStart && !campaign.IsGameOver && !campaign.IsComplete
        && world.Phase == GamePhase.Playing)
    {
        int fpsCx = sw / 2, fpsCy = sh / 2;
        int arm = Math.Max(8, (int)(12 * ui));
        int gap = Math.Max(3, (int)(4 * ui));
        var col = new Color(220, 220, 220, 180);
        Raylib.DrawRectangle(fpsCx - arm - gap, fpsCy - 1, arm, 2, col);
        Raylib.DrawRectangle(fpsCx + gap + 1,   fpsCy - 1, arm, 2, col);
        Raylib.DrawRectangle(fpsCx - 1, fpsCy - arm - gap, 2, arm, col);
        Raylib.DrawRectangle(fpsCx - 1, fpsCy + gap + 1,   2, arm, col);
        // HUD hint in top-left corner only.
        UiFont.DrawShadow($"FPS  F:exit", pad, pad + barH / 2, small, new Color(150, 150, 160, 180));
        return;
    }

    // Panel with a gold underline.
    Raylib.DrawRectangleGradientV(0, 0, sw, barH, new Color(0, 0, 0, 215), new Color(0, 0, 0, 120));
    Raylib.DrawRectangle(0, barH, sw, Math.Max(1, (int)(2 * ui)), new Color(214, 160, 30, 200));

    int row1 = pad, row2 = pad + (int)(28 * ui);
    UiFont.DrawShadow($"Level {campaign.LevelIndex + 1}", pad, row1, size, Color.Gold);

    int cx = sw / 2;
    UiFont.DrawCentered("SCORE", cx, row1, small, new Color(150, 160, 190, 255));
    UiFont.DrawCentered($"{campaign.Score:D7}", cx, row2 - (int)(4 * ui), size, Color.RayWhite);

    // Lives as hearts (numeric above 6 to avoid overflowing the bar).
    int heart = Math.Max(5, (int)(8 * ui));
    int lx = sw - pad - (int)(120 * ui);
    var heartColor = campaign.Lives <= 1 ? new Color(255, 70, 70, 255) : new Color(240, 60, 90, 255);
    if (campaign.Lives <= 6)
    {
        for (int i = 0; i < campaign.Lives; i++)
            DrawHeart(lx + i * (int)(heart * 2.6f) + heart, row1 + heart + (int)(2 * ui), heart, heartColor);
    }
    else
    {
        DrawHeart(lx + heart, row1 + heart + (int)(2 * ui), heart, heartColor);
        UiFont.DrawShadow($"x{campaign.Lives}", lx + heart * 3, row1, size, Color.RayWhite);
    }

    int enemies = world.Enemies.Count(e => e.IsAlive);
    string right = $"GOLD {world.GoldRemaining}  FOES {enemies}";
    UiFont.DrawShadow(right, sw - pad - UiFont.Measure(right, small), row2, small, Color.Gold);

    if (!menu.Active)
        UiFont.Draw("Esc/Start: menu   R: restart (costs a life)   C: camera   N/P: level",
        pad, sh - (int)(26 * ui), small, new Color(150, 150, 160, 255));
    if (args.Contains("--fps")) Raylib.DrawFPS(sw - (int)(90 * ui), barH + pad);
}

void DrawOverlays(int sw, int sh, float ui)
{
    if (!death.Ready) return; // the result plate waits for the death scene to play out
    if (campaign.IsComplete)
    {
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
        DrawCentered("ALL LEVELS CLEARED!", sw, sh / 2 - (int)(70 * ui), (int)(48 * ui), Color.Gold);
        DrawCentered($"Final score: {campaign.Score}", sw, sh / 2 - (int)(10 * ui), (int)(28 * ui), Color.RayWhite);
        DrawCentered($"Deaths: {campaign.Deaths}", sw, sh / 2 + (int)(26 * ui), (int)(24 * ui), Color.RayWhite);
        if (lastRank >= 0) DrawCentered($"NEW HIGH SCORE  #{lastRank + 1}", sw, sh / 2 + (int)(46 * ui), (int)(22 * ui), Color.SkyBlue);
        DrawCentered("Enter / R: play again    Esc: main menu", sw, sh / 2 + (int)(66 * ui), (int)(20 * ui), Color.Gray);
    }
    else if (campaign.IsGameOver)
    {
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
        DrawCentered("GAME OVER", sw, sh / 2 - (int)(70 * ui), (int)(56 * ui), Color.Red);
        DrawCentered($"Final score: {campaign.Score}", sw, sh / 2 - (int)(6 * ui), (int)(28 * ui), Color.RayWhite);
        DrawCentered($"Reached level {campaign.LevelIndex + 1}   Deaths: {campaign.Deaths}", sw, sh / 2 + (int)(30 * ui), (int)(22 * ui), Color.RayWhite);
        if (lastRank >= 0) DrawCentered($"NEW HIGH SCORE  #{lastRank + 1}", sw, sh / 2 + (int)(50 * ui), (int)(22 * ui), Color.Gold);
        DrawCentered("Enter / R: new game    Esc: main menu", sw, sh / 2 + (int)(70 * ui), (int)(20 * ui), Color.Gray);
    }
    else if (world.Phase == GamePhase.Lost)
    {
        DrawPlate(sw, sh / 2 - (int)(56 * ui), (int)(460 * ui), (int)(130 * ui), ui);
        DrawCentered("You died", sw, sh / 2 - (int)(40 * ui), (int)(44 * ui), Color.Red);
        DrawCentered($"Lives left: {campaign.Lives}", sw, sh / 2 + (int)(8 * ui), (int)(24 * ui), Color.SkyBlue);
        DrawCentered("Enter / R: retry", sw, sh / 2 + (int)(40 * ui), (int)(22 * ui), Color.RayWhite);
    }
    else if (world.Phase == GamePhase.Won)
    {
        bool last = campaign.LevelIndex + 1 >= levels.Count;
        DrawPlate(sw, sh / 2 - (int)(56 * ui), (int)(460 * ui), (int)(130 * ui), ui);
        DrawCentered("LEVEL COMPLETE!", sw, sh / 2 - (int)(40 * ui), (int)(44 * ui), Color.Gold);
        DrawCentered($"+{Campaign.LevelScore} and an extra life", sw, sh / 2 + (int)(8 * ui), (int)(22 * ui), Color.SkyBlue);
        DrawCentered(last ? "Enter: finish" : "Enter: next level", sw, sh / 2 + (int)(40 * ui), (int)(22 * ui), Color.RayWhite);
    }
    else if (awaitingStart)
    {
        if (introT < IntroDuration) return; // the plate appears once the camera has settled
        int bandH = (int)(150 * ui), top = sh / 2 - bandH / 2;
        DrawPlate(sw, top, (int)(520 * ui), bandH, ui);
        Raylib.DrawRectangle(sw / 2 - (int)(220 * ui), top + (int)(bandH * 0.46f), (int)(440 * ui), Math.Max(1, (int)(2 * ui)), new Color(214, 160, 30, 255));
        DrawCentered($"LEVEL {campaign.LevelIndex + 1}", sw, top + (int)(14 * ui), (int)(40 * ui), Color.Gold);
        if ((int)(Raylib.GetTime() * 2) % 2 == 0)
            DrawCentered("Press any key to start", sw, top + (int)(bandH * 0.46f) + (int)(14 * ui), (int)(20 * ui), Color.RayWhite);
    }
    else if (world.Grid.HiddenRevealed)
    {
        const string hint = "All gold collected - climb to the top!";
        int hintSize = (int)(24 * ui);
        DrawPlate(sw, (int)(66 * ui), UiFont.Measure(hint, hintSize) + (int)(40 * ui), hintSize + (int)(16 * ui), ui);
        DrawCentered(hint, sw, (int)(74 * ui), hintSize, Color.Gold);
    }
}

/// <summary>Dark rounded plate with a thin gold rim behind overlay text, so it stays readable over the busy wall.</summary>
static void DrawPlate(int sw, int y, int w, int h, float ui)
{
    int rim = Math.Max(1, (int)(2 * ui));
    float round = 0.18f;
    int x = (sw - w) / 2;
    Raylib.DrawRectangleRounded(new Rectangle(x - rim, y - rim, w + rim * 2, h + rim * 2), round, 8, new Color(214, 160, 30, 150));
    Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), round, 8, new Color(8, 9, 16, 225));
}

static void DrawCentered(string text, int sw, int y, int size, Color color)
{
    UiFont.DrawShadow(text, (sw - UiFont.Measure(text, size)) / 2, y, size, color);
}

static InputState ReadMovement(KeyBindings keys) => new(
    Left: keys.IsDown(GameAction.Left),
    Right: keys.IsDown(GameAction.Right),
    Up: keys.IsDown(GameAction.Up),
    Down: keys.IsDown(GameAction.Down));
