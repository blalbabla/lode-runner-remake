using System.Numerics;
using LodeRunner.Core.Simulation;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Purely visual death effects, driven by what the simulation reports (<see cref="GameWorld.DeathCause"/>):
/// blood, convulsions, a crushed hero, a guard waving its arms, slow motion and a camera push-in. Never touches the core.
/// </summary>
public sealed class DeathScene
{
    /// <summary>Slow-motion factor (1/3 speed).</summary>
    public const float SlowFactor = 1f / 3f;

    /// <summary>Real seconds after the death before the "You died" plate may show and a key may retry.</summary>
    public const float ReadyAfter = 1.9f;

    private const float ZoomInTime = 1.2f;
    private const float ZoomOutTime = 0.5f;

    private readonly Dictionary<int, CharacterRig.Fx> _enemyFx = new();
    private readonly Dictionary<int, float> _bloodAcc = new();
    private CharacterRig.Fx _playerFx;
    private DeathCause _cause;
    private int _killerId = -1;
    private float _zoomT;

    /// <summary>A death scene is playing (the simulation is frozen; the visuals keep going).</summary>
    public bool Active { get; private set; }

    /// <summary>Real seconds since the death.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Multiplier for simulation time: slow while the hero is stuck in a closing hole.</summary>
    public float SimScale { get; private set; } = 1f;

    /// <summary>Multiplier for particle time.</summary>
    public float VisualScale => Active || SimScale < 1f ? SlowFactor : 1f;

    /// <summary>0..1 smooth camera push-in toward the hero.</summary>
    public float Zoom => _zoomT * _zoomT * (3f - 2f * _zoomT);

    /// <summary>The death has played long enough for the result plate to appear and input to be accepted.</summary>
    public bool Ready => !Active || Elapsed >= ReadyAfter;

    /// <summary>The hero is inside a hole that is being bricked up right now (he dies when it closes).</summary>
    public bool HeroBricking { get; private set; }

    public CharacterRig.Fx PlayerFx => _playerFx;

    public CharacterRig.Fx EnemyFx(int id) => _enemyFx.TryGetValue(id, out var fx) ? fx : default;

    /// <summary>New world loaded. A fresh level starts without zoom; a retry lets the camera ease back out.</summary>
    public void Reset(bool snapCamera)
    {
        Active = false;
        HeroBricking = false;
        Elapsed = 0f;
        SimScale = 1f;
        _cause = DeathCause.None;
        _killerId = -1;
        _playerFx = default;
        _enemyFx.Clear();
        _bloodAcc.Clear();
        if (snapCamera) _zoomT = 0f;
    }

    /// <summary>The player has just died: opening burst of the scene.</summary>
    public void Start(GameWorld world, ParticleSystem particles)
    {
        Active = true;
        Elapsed = 0f;
        _cause = world.DeathCause;
        _killerId = world.KillerId;

        var grid = world.Grid;
        var hero = Center(world.Player, grid);
        switch (_cause)
        {
            case DeathCause.Crushed:
                particles.Blood(hero, 130, 4.2f, upward: 1.5f, spreadX: 1.3f);
                break;
            case DeathCause.GuardFellOnHero:
                particles.Blood(hero + new Vector3(0f, -0.3f, 0f), 150, 4.4f, upward: 0.35f, spreadX: 2.4f);
                break;
            case DeathCause.HeroFellOnGuard:
                particles.Blood(KillerCenter(world) + new Vector3(0f, 0.1f, 0f), 160, 4.4f, upward: 1.8f, spreadX: 1.1f);
                break;
            default:
                particles.Blood(hero, 120, 4.2f, upward: 1.0f, spreadX: 1.4f);
                break;
        }
    }

    /// <summary>Per-frame update; <paramref name="realDt"/> is wall-clock seconds.</summary>
    public void Update(GameWorld world, ParticleSystem particles, float realDt)
    {
        var grid = world.Grid;
        float dt = realDt * VisualScale;
        _enemyFx.Clear();

        bool heroStuck = world.Phase == GamePhase.Playing && InRefillingHole(world.Player, grid);
        SimScale = heroStuck ? SlowFactor : 1f;
        HeroBricking = heroStuck;

        // ---- hero ----
        var fx = default(CharacterRig.Fx);
        if (heroStuck)
        {
            fx = new CharacterRig.Fx(Jitter: 1.4f);
            EmitRate(-1, Center(world.Player, grid), 110f, dt, particles, 3.2f, 1.6f, 0.9f);
        }
        else if (Active)
        {
            float t = Elapsed;
            switch (_cause)
            {
                case DeathCause.Crushed:
                    EmitRate(-1, Center(world.Player, grid), t < 0.8f ? 60f : 0f, dt, particles, 3.0f, 1.8f, 1.2f);
                    break;
                case DeathCause.Caught:
                    fx = new CharacterRig.Fx(Jitter: Math.Clamp(1.2f - t * 0.25f, 0.6f, 1.2f));
                    EmitRate(-1, Center(world.Player, grid), t < 1.4f ? 70f : 0f, dt, particles, 3.0f, 1.0f, 1.3f);
                    break;
                case DeathCause.GuardFellOnHero:
                    fx = new CharacterRig.Fx(Crush: Smooth(t / 0.22f));
                    EmitRate(-1, Center(world.Player, grid) + new Vector3(0f, -0.35f, 0f), t < 1.0f ? 60f : 0f,
                        dt, particles, 2.2f, 0.3f, 2.2f);
                    break;
                case DeathCause.HeroFellOnGuard:
                {
                    // The hero drops into the guard as if into a grinder: he is squashed while sinking into the monk.
                    float k = Smooth(t / 0.45f);
                    var toKiller = KillerCenter(world) - Center(world.Player, grid);
                    fx = new CharacterRig.Fx(Crush: k, Offset: toKiller * (0.78f * k));
                    EmitRate(-1, KillerCenter(world) + new Vector3(0f, 0.15f, 0f), t < 1.4f ? 120f : 0f,
                        dt, particles, 3.6f, 1.8f, 1.0f);
                    break;
                }
            }
        }
        _playerFx = fx;

        // ---- guards ----
        foreach (var e in world.Enemies)
        {
            if (!e.IsAlive) continue;
            var efx = default(CharacterRig.Fx);
            if (InRefillingHole(e, grid))
            {
                efx = new CharacterRig.Fx(Jitter: 1.4f);
                EmitRate(e.Id, Center(e, grid), 90f, dt, particles, 3.0f, 1.5f, 0.9f);
            }
            else if (Active && e.Id == _killerId && _cause != DeathCause.GuardFellOnHero)
            {
                // The killer thrashes its arms wildly.
                efx = new CharacterRig.Fx(Flail: 1f, Jitter: _cause == DeathCause.HeroFellOnGuard ? 0.6f : 0.15f);
            }
            if (efx != default) _enemyFx[e.Id] = efx;
        }

        // ---- clocks ----
        if (Active) Elapsed += realDt;
        bool wantZoom = Active || heroStuck;
        _zoomT = Math.Clamp(_zoomT + (wantZoom ? realDt / ZoomInTime : -realDt / ZoomOutTime), 0f, 1f);
    }

    /// <summary>Pushes the camera toward the hero, along its own viewing direction.</summary>
    public Raylib_cs.Camera3D ApplyCamera(Raylib_cs.Camera3D cam, Vector3 hero)
    {
        float k = Zoom;
        if (k <= 0f || cam.Projection != Raylib_cs.CameraProjection.Perspective) return cam;

        var back = cam.Position - cam.Target;
        float dist = back.Length();
        if (dist < 1e-3f) return cam;
        var dir = back / dist;

        var focus = hero + new Vector3(0f, 0.15f, 0f);
        var nearPos = focus + dir * (dist * 0.2f);
        cam.Position = Vector3.Lerp(cam.Position, nearPos, k);
        cam.Target = Vector3.Lerp(cam.Target, focus, k);
        return cam;
    }

    // ---- helpers ----

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector3 Center(Entity e, Grid grid) =>
        GridCoords.EntityCenter(e.X, e.Y, e.X, e.Y, 1f, grid.TileUnits, grid.Rows);

    private Vector3 KillerCenter(GameWorld world)
    {
        foreach (var e in world.Enemies)
            if (e.Id == _killerId) return Center(e, world.Grid);
        return Center(world.Player, world.Grid);
    }

    /// <summary>The entity's centre sits in a hole that is being bricked up again.</summary>
    private static bool InRefillingHole(Entity e, Grid grid)
    {
        int t = grid.TileUnits;
        int col = (e.X + t / 2) / t, row = (e.Y + t / 2) / t;
        return grid.Hole(col, row).State == HoleState.Refilling;
    }

    /// <summary>Continuous spray: <paramref name="rate"/> drops per second, accumulated across frames.</summary>
    private void EmitRate(int key, Vector3 pos, float rate, float dt, ParticleSystem particles,
        float speed, float upward, float spreadX)
    {
        if (rate <= 0f) return;
        _bloodAcc.TryGetValue(key, out float acc);
        acc += rate * dt;
        int n = (int)acc;
        acc -= n;
        _bloodAcc[key] = acc;
        if (n > 0) particles.Blood(pos, n, speed, upward, spreadX);
    }
}
