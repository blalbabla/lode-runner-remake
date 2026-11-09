using System.Numerics;
using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Purely visual particles driven by simulation events. Uses its own RNG and wall-clock time, so it never
/// touches the deterministic core.
/// </summary>
public sealed class ParticleSystem
{
    private struct Particle
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public Color Color;
        public float Gravity;
    }

    private struct Puff
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public Color Color;
    }

    /// <summary>A small spinning brick fragment thrown out by a dig blast.</summary>
    private struct Chunk
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public Vector3 Axis;
        public float Angle;
        public float Spin;
        public Vector3 Size;
        public float Life;
        public float MaxLife;
        public Color Color;
    }

    private struct Flash
    {
        public Vector3 Pos;
        public float Life;
        public float MaxLife;
        public float Size;
    }

    private readonly List<Chunk> _chunks = new();
    private readonly List<Flash> _flashes = new();
    private readonly List<Particle> _particles = new();
    private readonly List<Puff> _puffs = new();
    private readonly List<Drop> _drops = new();
    private readonly Random _rng = new(12345);

    private const int MaxDrops = 1800;

    /// <summary>A small round blood droplet.</summary>
    private struct Drop
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public float Life;
        public float MaxLife;
        public float Radius;
        public Color Color;
    }

    public void Clear()
    {
        _particles.Clear();
        _puffs.Clear();
        _chunks.Clear();
        _flashes.Clear();
        _drops.Clear();
    }

    /// <summary>
    /// Spray of small red balls. <paramref name="upward"/> biases the spray up (a fountain), <paramref name="spreadX"/>
    /// widens it sideways (a splat). Drops start in front of the walls (<paramref name="z"/>) so they stay visible
    /// even when they leave a brick cell.
    /// </summary>
    public void Blood(Vector3 origin, int count, float speed, float upward = 1f, float spreadX = 1f, float z = 0.55f)
    {
        for (int i = 0; i < count && _drops.Count < MaxDrops; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float r = 0.25f + (float)_rng.NextDouble() * 0.75f;
            float vx = MathF.Cos(a) * speed * r * spreadX;
            float vy = (MathF.Sin(a) * 0.5f + upward * (0.3f + (float)_rng.NextDouble() * 0.7f)) * speed * r;
            float vz = (float)_rng.NextDouble() * speed * 0.2f;
            float tone = 0.65f + (float)_rng.NextDouble() * 0.35f;
            bool blob = _rng.NextDouble() < 0.08;
            float life = 0.9f + (float)_rng.NextDouble() * 0.9f;
            _drops.Add(new Drop
            {
                Pos = origin + new Vector3(((float)_rng.NextDouble() - 0.5f) * 0.25f, ((float)_rng.NextDouble() - 0.5f) * 0.25f, z - origin.Z),
                Vel = new Vector3(vx, vy, vz),
                Life = life,
                MaxLife = life,
                Radius = blob ? 0.065f : 0.02f + (float)_rng.NextDouble() * 0.035f,
                Color = new Color((byte)(210 * tone), (byte)(14 * tone), (byte)(22 * tone), (byte)255),
            });
        }
    }


    /// <summary>
    /// Small blast where the brick stood: a flash, a shower of spinning brick fragments, fine grit and a dust cloud.
    /// Fragments keep a mostly upward/outward-to-camera path so they clear the neighbouring walls.
    /// </summary>
    private void BrickBlast(Vector3 c)
    {
        _flashes.Add(new Flash { Pos = c + new Vector3(0f, 0.1f, 0.35f), Life = 0.16f, MaxLife = 0.16f, Size = 1.5f });

        for (int i = 0; i < 18; i++)
        {
            var axis = new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f);
            if (axis.LengthSquared() < 1e-4f) axis = Vector3.UnitY;
            float tone = 0.75f + (float)_rng.NextDouble() * 0.5f;
            bool dark = _rng.NextDouble() < 0.3;
            var baseCol = dark ? new Vector3(120f, 62f, 44f) : new Vector3(186f, 104f, 72f);
            float life = 0.6f + (float)_rng.NextDouble() * 0.45f;
            _chunks.Add(new Chunk
            {
                Pos = c + new Vector3((float)(_rng.NextDouble() - 0.5) * 0.6f, (float)(_rng.NextDouble() - 0.5) * 0.4f, (float)_rng.NextDouble() * 0.4f),
                Vel = new Vector3(((float)_rng.NextDouble() * 2f - 1f) * 1.5f, 2.2f + (float)_rng.NextDouble() * 2.8f, 0.6f + (float)_rng.NextDouble() * 1.8f),
                Axis = Vector3.Normalize(axis),
                Angle = (float)_rng.NextDouble() * 360f,
                Spin = ((float)_rng.NextDouble() * 2f - 1f) * 720f,
                Size = new Vector3(0.09f + (float)_rng.NextDouble() * 0.14f, 0.05f + (float)_rng.NextDouble() * 0.07f, 0.08f + (float)_rng.NextDouble() * 0.06f),
                Life = life,
                MaxLife = life,
                Color = new Color((byte)Math.Min(255f, baseCol.X * tone), (byte)Math.Min(255f, baseCol.Y * tone), (byte)Math.Min(255f, baseCol.Z * tone), (byte)255),
            });
        }

        Burst(c + new Vector3(0f, 0.1f, 0.2f), 14, new Color(170, 96, 66, 255), 2.2f, 0.55f, 0.04f, 9f); // fine grit
        DustCloud(c + new Vector3(0f, 0.1f, 0f), 12, 0.6f);
    }

    /// <summary>Soft dust/smoke cloud: puffs grow while fading, drift up and outward.</summary>
    private void DustCloud(Vector3 center, int count, float spread)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float s = spread * (0.3f + (float)_rng.NextDouble() * 0.7f);
            int shade = 105 + _rng.Next(0, 45);
            _puffs.Add(new Puff
            {
                Pos = center + new Vector3((float)(_rng.NextDouble() - 0.5) * 0.5f, (float)(_rng.NextDouble() - 0.3) * 0.3f, 0.2f),
                Vel = new Vector3(MathF.Cos(a) * s, 0.15f + (float)_rng.NextDouble() * 0.35f, 0f),
                Life = 0.7f + (float)_rng.NextDouble() * 0.5f,
                MaxLife = 1.2f,
                Size = 0.35f + (float)_rng.NextDouble() * 0.25f,
                Color = new Color((byte)(shade + 20), (byte)shade, (byte)(shade - 20), (byte)255),
            });
        }
    }


    public void OnEvent(WorldEvent ev, int gridRows)
    {
        var c = GridCoords.CellCenter(ev.Cell.Col, ev.Cell.Row, gridRows);
        switch (ev.Kind)
        {
            case WorldEventKind.GoldCollected:
            case WorldEventKind.EnemyGoldTaken:
                Burst(c, 10, new Color(255, 215, 64, 255), 1.6f, 0.55f, 0.07f, 3f);
                Burst(c, 6, new Color(255, 250, 200, 255), 1.0f, 0.7f, 0.045f, -0.6f); // floating sparkles
                break;
            case WorldEventKind.EnemyGoldDropped:
                Burst(c, 5, new Color(255, 215, 64, 255), 1f, 0.4f, 0.05f, 3f);
                break;
            case WorldEventKind.HoleOpened:
                // The energy ball lands as this event fires: the brick bursts into fragments and dust.
                BrickBlast(c);
                break;
            case WorldEventKind.HoleClosed:
                Burst(c, 10, new Color(190, 110, 70, 255), 1.5f, 0.55f, 0.08f, 6f);
                break;
            case WorldEventKind.EnemyTrapped:
                Burst(c, 6, new Color(210, 210, 210, 255), 1.1f, 0.4f, 0.06f, 4f);
                break;
            case WorldEventKind.EnemyDied:
                Blood(c + new Vector3(0f, 0.1f, 0f), 60, 3.6f, 1.2f);
                break;
            case WorldEventKind.EnemyRespawned:
                Burst(c, 14, new Color(120, 220, 255, 255), 1.8f, 0.7f, 0.07f, -0.5f);
                break;
            case WorldEventKind.PlayerDied:
                break; // the death scene (DeathScene) sprays blood according to how the hero died
            case WorldEventKind.LevelWon:
                Burst(c, 40, new Color(255, 215, 64, 255), 3.5f, 1.3f, 0.09f, 3f);
                Burst(c, 24, new Color(120, 220, 255, 255), 2.8f, 1.2f, 0.08f, 3f);
                break;
        }
    }

    /// <summary>Golden sparkles rising from every ladder that just appeared.</summary>
    public void RevealSparkle(GameWorld world)
    {
        var level = world.Level;
        for (int r = 0; r < level.Rows; r++)
            for (int c = 0; c < level.Cols; c++)
                if (level[c, r] == TileType.HiddenLadder)
                {
                    var pos = GridCoords.CellCenter(c, r, level.Rows);
                    Burst(pos, 8, new Color(255, 225, 110, 255), 0.9f, 1.0f, 0.06f, -0.8f);
                    Burst(pos, 4, new Color(255, 255, 255, 255), 0.6f, 0.8f, 0.04f, -0.4f);
                }
    }

    private void Burst(Vector3 center, int count, Color color, float speed, float life, float size, float gravity)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float up = 0.3f + (float)_rng.NextDouble() * 0.9f;
            float s = speed * (0.4f + (float)_rng.NextDouble() * 0.6f);
            _particles.Add(new Particle
            {
                Pos = center,
                Vel = new Vector3(MathF.Cos(a) * s, up * s, MathF.Sin(a) * s * 0.5f),
                Life = life * (0.6f + (float)_rng.NextDouble() * 0.4f),
                MaxLife = life,
                Size = size,
                Color = color,
                Gravity = gravity,
            });
        }
    }

    public void Update(float dt)
    {
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Life -= dt;
            if (d.Life <= 0f)
            {
                _drops.RemoveAt(i);
                continue;
            }
            d.Vel.Y -= 11f * dt;
            d.Pos += d.Vel * dt;
            _drops[i] = d;
        }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.Vel.Y -= p.Gravity * dt;
            p.Pos += p.Vel * dt;
            _particles[i] = p;
        }

        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            var ch = _chunks[i];
            ch.Life -= dt;
            if (ch.Life <= 0f)
            {
                // The fragment dissolves into a small dust puff.
                _puffs.Add(new Puff
                {
                    Pos = ch.Pos,
                    Vel = new Vector3(ch.Vel.X * 0.15f, 0.1f, 0f),
                    Life = 0.5f,
                    MaxLife = 0.8f,
                    Size = 0.22f,
                    Color = new Color((byte)135, (byte)118, (byte)100, (byte)255),
                });
                _chunks.RemoveAt(i);
                continue;
            }
            ch.Vel.Y -= 9.5f * dt;
            ch.Vel.X *= MathF.Max(0f, 1f - 0.8f * dt);
            ch.Pos += ch.Vel * dt;
            ch.Angle += ch.Spin * dt;
            _chunks[i] = ch;
        }

        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            f.Life -= dt;
            if (f.Life <= 0f) _flashes.RemoveAt(i);
            else _flashes[i] = f;
        }

        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var p = _puffs[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _puffs.RemoveAt(i);
                continue;
            }
            p.Vel *= MathF.Max(0f, 1f - 1.8f * dt);
            p.Pos += p.Vel * dt;
            _puffs[i] = p;
        }
    }

    /// <summary>Draw inside Mode3D and outside the lighting shader, so particles stay at full brightness.</summary>
    public void Draw()
    {
        if (_flashes.Count > 0)
        {
            SoftSprite.BeginAdditive();
            foreach (var f in _flashes)
            {
                float k = Math.Clamp(f.Life / f.MaxLife, 0f, 1f);
                SoftSprite.Draw(f.Pos, f.Size * (1.4f - 0.5f * k), new Color((byte)255, (byte)150, (byte)60, (byte)(150 * k)));
                SoftSprite.Draw(f.Pos, f.Size * 0.5f, new Color((byte)255, (byte)230, (byte)170, (byte)(220 * k)));
            }
            SoftSprite.End();
        }

        foreach (var ch in _chunks)
        {
            // Shrink away over the last third of the life.
            float k = Math.Clamp(ch.Life / (ch.MaxLife * 0.35f), 0f, 1f);
            Rlgl.PushMatrix();
            Rlgl.Translatef(ch.Pos.X, ch.Pos.Y, ch.Pos.Z);
            Rlgl.Rotatef(ch.Angle, ch.Axis.X, ch.Axis.Y, ch.Axis.Z);
            Raylib.DrawCube(Vector3.Zero, ch.Size.X * k, ch.Size.Y * k, ch.Size.Z * k, ch.Color);
            Rlgl.PopMatrix();
        }

        if (_puffs.Count > 0)
        {
            SoftSprite.BeginAlpha();
            foreach (var p in _puffs)
            {
                float k = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
                float grow = 1f + (1f - k) * 1.2f;
                float alpha = Math.Min(1f, k * 1.6f) * 0.55f;
                SoftSprite.Draw(p.Pos, p.Size * grow, new Color(p.Color.R, p.Color.G, p.Color.B, (byte)(alpha * 255f)));
            }
            SoftSprite.End();
        }

        foreach (var d in _drops)
        {
            float k = Math.Clamp(d.Life / (d.MaxLife * 0.3f), 0f, 1f); // shrink away over the last third
            Raylib.DrawSphereEx(d.Pos, d.Radius * (0.4f + 0.6f * k), 5, 6, d.Color);
        }

        foreach (var p in _particles)
        {
            float k = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
            float s = p.Size * (0.4f + 0.6f * k);
            var color = new Color(p.Color.R, p.Color.G, p.Color.B, (byte)(255 * Math.Min(1f, k * 1.5f)));
            Raylib.DrawCube(p.Pos, s, s, s, color);
        }
    }
}
