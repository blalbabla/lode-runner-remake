using System.Numerics;
using LodeRunner.Core.Levels;
using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>Draws a <see cref="LevelData"/> with Raylib primitives. Read-only view of the data.</summary>
public sealed class WorldRenderer
{
    private static readonly Color RopeA = new(214, 178, 108, 255);
    private static readonly Color RopeB = new(158, 120, 66, 255);
    private static readonly Color GoldShine = new(255, 244, 168, 255);
    private static readonly Color Ghost = new(120, 200, 255, 90);

    private const float LadderDepth = 0.3f; // Z offset toward the back wall (negative Z = away from camera)

    private readonly LightingShader _light;
    private readonly CharacterRig _rig = new();
    private readonly List<Vector3> _glow = new();
    private bool _shadowPass;

    private void Mat(SurfaceMaterial m)
    {
        if (!_shadowPass) _light.SetMaterial(m);
    }

    public WorldRenderer(LightingShader light) => _light = light;

    /// <summary>Skips the hero in the visible pass (his shadow still falls); set while the camera is inside or at his head.</summary>
    public bool HidePlayer { get; set; }

    /// <summary>Death-scene pose modifiers (convulsions, crushed hero, waving guard); null for none.</summary>
    public DeathScene? Death { get; set; }

    /// <param name="alpha">Fraction of the way from the previous tick to the current one, in [0, 1].</param>
    /// <param name="shadowPass">Depth-only pass into the shadow map: no backdrop, no shader or material changes.</param>
    public void Draw(GameWorld world, float time, float alpha, bool shadowPass = false)
    {
        _shadowPass = shadowPass;
        var grid = world.Grid;

        // One pass per material: switching the shader material flushes the batch, so keep switches few.
        Mat(SurfaceMaterial.Plain);
        if (!_shadowPass)
        {
            DrawBackdrop(grid.Cols, grid.Rows);
            Mat(SurfaceMaterial.Plain);
            CollectGlow(world);
            _light.SetGlowLights(CollectionsMarshalSpan(_glow));
        }

        Mat(SurfaceMaterial.Brick);
        ForEachCell(grid, (c, r, center) =>
        {
            var hole = grid.Hole(c, r);
            if (hole.State != HoleState.Intact) DrawHoleBrick(center, c, r, hole);
            else if (grid.Tile(c, r) == TileType.Brick || (grid.Tile(c, r) == TileType.Empty && grid.IsTrap(c, r)))
                DrawBrick(center, c, r, 1f);
        });

        Mat(SurfaceMaterial.Steel);
        ForEachCell(grid, (c, r, center) =>
        {
            if (grid.Hole(c, r).State == HoleState.Intact && grid.Tile(c, r) == TileType.Concrete) DrawConcrete(center);
        });

        Mat(SurfaceMaterial.Metal);
        ForEachCell(grid, (c, r, center) =>
        {
            if (grid.Hole(c, r).State == HoleState.Intact && grid.Tile(c, r) == TileType.Ladder)
                DrawLadder(center, solid: true, time, c, r);
        });

        Mat(SurfaceMaterial.Gold);
        ForEachCell(grid, (c, r, center) =>
        {
            if (grid.Hole(c, r).State == HoleState.Intact && world.IsGold(c, r)) DrawGold(center, time, Hash(c, r));
        });

        Mat(SurfaceMaterial.Plain);
        ForEachCell(grid, (c, r, center) =>
        {
            if (grid.Hole(c, r).State != HoleState.Intact) return;
            if (grid.Tile(c, r) == TileType.Rope) DrawRope(center);
            if (!_shadowPass && world.IsGold(c, r)) { DrawHalo(center, time, Hash(c, r)); DrawGlint(center, time, Hash(c, r)); }
        });

        Mat(SurfaceMaterial.Clay);
        foreach (var e in world.Enemies)
        {
            if (!e.IsAlive) continue;
            DrawEnemy(e, GridCoords.EntityCenter(e.PrevX, e.PrevY, e.X, e.Y, alpha, grid.TileUnits, grid.Rows), time,
                Death?.EnemyFx(e.Id) ?? default);
        }

        var p = world.Player;
        bool heroHidden = HidePlayer && !_shadowPass; // intro shot starts inside the hero's head
        if (!heroHidden && (world.Phase == GamePhase.Playing || !IsInsideClosedBrick(world)))
            DrawPlayer(p, GridCoords.EntityCenter(p.PrevX, p.PrevY, p.X, p.Y, alpha, grid.TileUnits, grid.Rows), time);

        Mat(SurfaceMaterial.Plain);
        // Translucent parts last so they blend over everything behind them.
        ForEachCell(grid, (c, r, center) =>
        {
            if (!_shadowPass && grid.Hole(c, r).State == HoleState.Intact && grid.Tile(c, r) == TileType.Empty && grid.IsHiddenLadder(c, r))
                DrawLadder(center, solid: false, time, c, r);
        });
    }

    private static void ForEachCell(Grid grid, Action<int, int, Vector3> visit)
    {
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                visit(c, r, GridCoords.CellCenter(c, r, grid.Rows));
    }

    private static ReadOnlySpan<Vector3> CollectionsMarshalSpan(List<Vector3> list) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);

    /// <summary>Gold on the floor lights its surroundings; with more than the shader limit, the pieces nearest the player win.</summary>
    private void CollectGlow(GameWorld world)
    {
        _glow.Clear();
        var grid = world.Grid;
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Cols; c++)
                if (world.IsGold(c, r) && grid.Hole(c, r).State == HoleState.Intact)
                    _glow.Add(GridCoords.CellCenter(c, r, grid.Rows) + new Vector3(0f, -0.25f, 0.35f));
        if (_glow.Count <= LightingShader.MaxGlowLights) return;

        var pc = world.PlayerCell;
        var pp = GridCoords.CellCenter(pc.Col, pc.Row, grid.Rows);
        _glow.Sort((a, b) => Vector3.DistanceSquared(a, pp).CompareTo(Vector3.DistanceSquared(b, pp)));
        _glow.RemoveRange(LightingShader.MaxGlowLights, _glow.Count - LightingShader.MaxGlowLights);
    }

    /// <summary>A player crushed by a closing hole is hidden inside the brick instead of poking through it.</summary>
    private static bool IsInsideClosedBrick(GameWorld world)
    {
        var cell = world.PlayerCell;
        return world.Grid.IsSolid(cell.Col, cell.Row);
    }

    // ---- helpers ----

    private static int Hash(int c, int r)
    {
        unchecked
        {
            uint h = (uint)(c * 73856093) ^ (uint)(r * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }

    private static Color Scale(Color c, float k) => new(
        (byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255),
        (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    // ---- environment ----

    /// <summary>
    /// Light wall of large bricks behind the playfield: one slab spanning the level; brick pattern, mortar and
    /// bump mapping come from the shader (<see cref="SurfaceMaterial.Wall"/>), keyed to world position.
    /// </summary>
    private void DrawBackdrop(int cols, int rows)
    {
        Mat(SurfaceMaterial.Wall);
        Raylib.DrawCube(new Vector3(cols / 2f, rows / 2f, -0.62f), cols, rows, 0.1f, Color.White);
    }

    /// <summary>
    /// Brick block as one cube; the brick pattern, mortar and bump mapping come from the shader, keyed to world
    /// position, so a block shrinking in a hole cycle keeps its pattern.
    /// </summary>
    private static void DrawBrick(Vector3 center, int col, int row, float height)
    {
        float bottom = center.Y - 0.5f;
        Raylib.DrawCube(new Vector3(center.X, bottom + height / 2f, center.Z), 1f, height, 1f, Color.White);
    }

    private static void DrawConcrete(Vector3 center) => Raylib.DrawCube(center, 1f, 1f, 1f, Color.White);

    /// <summary>
    /// Brick in a hole cycle. Opening: the brick sinks; Open: empty (gold may lie in the pit);
    /// Refilling: the brick grows back from the bottom.
    /// </summary>
    private static void DrawHoleBrick(Vector3 center, int col, int row, HoleInfo hole)
    {
        if (hole.State == HoleState.Refilling)
        {
            DrawRefillingBricks(center, hole.Progress);
            return;
        }

        // Opening: the brick stays whole (with a growing tremor) until the energy ball hits;
        // the blast particles then take its place the moment the hole opens.
        if (hole.State != HoleState.Opening) return;
        float shake = MathF.Max(0f, hole.Progress - 0.4f) * 0.04f * MathF.Sin((float)Raylib.GetTime() * 80f);
        DrawBrick(center + new Vector3(shake, 0f, 0f), col, row, 1f);
    }

    // Brick edges (x offsets inside the cell) per course. They match the shader's running bond:
    // even courses hold two bricks, odd courses are shifted by a quarter and hold half-bricks at both ends.
    private static readonly float[] EvenCourseEdges = [0f, 0.5f, 1f];
    private static readonly float[] OddCourseEdges = [0f, 0.25f, 0.75f, 1f];
    private const int CourseCount = 4;

    /// <summary>
    /// Refill: the wall is laid brick by brick, course by course from the bottom, left to right.
    /// Each brick shoots up over its own slice of the refill time; the bricks before it are complete.
    /// </summary>
    private static void DrawRefillingBricks(Vector3 center, float progress)
    {
        const float courseH = 1f / CourseCount;
        const int total = 2 + 3 + 2 + 3; // bricks in a full cell
        float pos = Math.Clamp(progress, 0f, 1f) * total;
        float bottom = center.Y - 0.5f;
        int index = 0;

        for (int k = 0; k < CourseCount; k++)
        {
            float[] edges = k % 2 == 0 ? EvenCourseEdges : OddCourseEdges;
            for (int i = 0; i < edges.Length - 1; i++, index++)
            {
                float t = Math.Clamp(pos - index, 0f, 1f);
                if (t <= 0.01f) return; // later bricks are not laid yet
                float h = courseH * t;
                float width = edges[i + 1] - edges[i];
                float x = center.X - 0.5f + edges[i] + width / 2f;
                Raylib.DrawCube(new Vector3(x, bottom + k * courseH + h / 2f, center.Z), width, h, 1f, Color.White);
            }
        }
    }

    private static void DrawLadder(Vector3 center, bool solid, float time, int col, int row)
    {
        // Push the ladder toward the back wall so it hugs the far side of the corridor
        // instead of standing in the middle. Negative Z = away from camera.
        center = center with { Z = center.Z - LadderDepth };

        const float railX = 0.32f;
        const float railR = 0.055f;
        Color rail = solid ? Color.White : Ghost;
        Color rung = solid ? Color.White : Ghost;
        int sides = solid ? 14 : 6;

        foreach (float sx in new[] { -railX, railX })
        {
            var bottom = center + new Vector3(sx, -0.5f, 0f);
            var top = center + new Vector3(sx, 0.5f, 0f);
            Raylib.DrawCylinderEx(bottom, top, railR, railR, sides, rail);
        }

        const int rungs = 4;
        for (int i = 0; i < rungs; i++)
        {
            float y = -0.5f + (i + 0.5f) / rungs;
            var a = center + new Vector3(-railX, y, 0f);
            var b = center + new Vector3(railX, y, 0f);
            Raylib.DrawCylinderEx(a, b, 0.036f, 0.036f, solid ? 10 : 5, rung);
        }

        if (!solid)
        {
            // A hidden ladder shimmers faintly so the player can notice it is there.
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 3f + (col + row));
            Raylib.DrawCube(center, railX * 2f + railR * 2f, 1f, 0.05f, new Color(150, 220, 255, (int)(10 + 20 * pulse)));
        }
    }

    private static void DrawRope(Vector3 center)
    {
        // Braided look: short alternating-colour segments with a slight zig-zag.
        float y = center.Y + 0.35f;
        const int segs = 8;
        const float seg = 1f / segs;
        for (int i = 0; i < segs; i++)
        {
            float x0 = center.X - 0.5f + i * seg;
            float wob = (i % 2 == 0 ? 1f : -1f) * 0.012f;
            var from = new Vector3(x0, y + wob, center.Z);
            var to = new Vector3(x0 + seg * 1.05f, y - wob, center.Z);
            Raylib.DrawCylinderEx(from, to, 0.05f, 0.05f, 6, i % 2 == 0 ? RopeA : RopeB);
        }
    }

    // ---- gold ----

    private static void DrawGold(Vector3 center, float time, int hash)
    {
        float yaw = MathF.Sin(time * 1.4f + (hash & 0xFF)) * 38f;
        DrawIngot(center + new Vector3(0f, -0.5f, 0f), 1f, yaw);
    }

    /// <summary>Periodic star-shaped glint on a gold piece (unlit, drawn with the plain material).</summary>
    private static void DrawGlint(Vector3 center, float time, int hash)
    {
        float phase = (time * 0.7f + (hash & 0xFF) * 0.137f) % 1f;
        if (phase >= 0.22f) return;
        float s = MathF.Sin(phase / 0.22f * MathF.PI);
        var g = center + new Vector3(0.14f, -0.22f, 0.26f);
        Raylib.DrawCube(g, 0.24f * s, 0.035f * s, 0.02f, Color.White);
        Raylib.DrawCube(g, 0.035f * s, 0.24f * s, 0.02f, Color.White);
    }

    /// <summary>Soft additive golden halo behind a gold piece, pulsing slowly.</summary>
    private static void DrawHalo(Vector3 center, float time, int hash)
    {
        float pulse = 0.85f + 0.15f * MathF.Sin(time * 2.2f + (hash & 0xFF));
        var c = center + new Vector3(0f, -0.3f, 0.12f);
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Additive);
        const int rings = 7;
        for (int i = 0; i < rings; i++)
        {
            float t = i / (float)rings;
            float radius = (0.12f + 0.62f * t) * pulse;
            int a = (int)(20 * (1f - t) * (1f - t) + 2);
            Raylib.DrawCylinderEx(c, c + new Vector3(0f, 0f, 0.002f), radius, radius, 24, new Color(255, 180, 60, a));
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    /// <summary>Bevelled ingot standing on <paramref name="floor"/> (bottom centre), yawed around the Y axis.</summary>
    private static void DrawIngot(Vector3 floor, float scale, float yawDeg)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(floor.X, floor.Y, floor.Z);
        Rlgl.Rotatef(yawDeg, 0f, 1f, 0f);
        Rlgl.Scalef(scale, scale, scale);
        Raylib.DrawCube(new Vector3(0f, 0.07f, 0f), 0.62f, 0.14f, 0.36f, Color.White);
        Raylib.DrawCube(new Vector3(0f, 0.20f, 0f), 0.46f, 0.12f, 0.26f, Color.White);
        Raylib.DrawCube(new Vector3(0f, 0.265f, 0f), 0.30f, 0.02f, 0.14f, Color.White);
        Rlgl.PopMatrix();
    }

    // ---- characters ----

    private void DrawPlayer(Player p, Vector3 center, float time)
    {
        _rig.DrawPlayer(p, center, time, _shadowPass, Death?.PlayerFx ?? default);
    }

    /// <summary>
    /// Glowing energy ball flying from the player's hands into the brick being dug. Unlit: call after the lighting
    /// shader has ended, inside Mode3D.
    /// </summary>
    public static void DrawEffects(GameWorld world, float time, float alpha)
    {
        var p = world.Player;
        if (p.Pose != Pose.Dig || p.DigTicksLeft <= 0) return;

        var grid = world.Grid;
        var center = GridCoords.EntityCenter(p.PrevX, p.PrevY, p.X, p.Y, alpha, grid.TileUnits, grid.Rows);
        float dir = p.Facing == Facing.Right ? 1f : -1f;

        // Progress advances per tick; smooth it with the render alpha.
        float total = world.Config.DigTicks;
        float t = Math.Clamp(1f - (p.DigTicksLeft - alpha) / total, 0f, 1f);
        var from = center + new Vector3(dir * 0.28f, -0.02f, 0.12f);
        var to = new Vector3(center.X + dir * 1.0f, center.Y - 0.85f, center.Z + 0.10f);
        var pos = Vector3.Lerp(from, to, t * t * (3f - 2f * t));

        float flick = 0.85f + 0.15f * MathF.Sin(time * 50f);
        var dirVec = Vector3.Normalize(to - from);

        SoftSprite.BeginAdditive();
        // Trail: fading glows behind the ball.
        for (int i = 4; i >= 1; i--)
        {
            float k = 1f - i / 5f;
            var tp = pos - dirVec * (0.07f * i);
            SoftSprite.Draw(tp, 0.26f * k, new Color((byte)255, (byte)170, (byte)60, (byte)(90 * k)));
        }
        SoftSprite.Draw(pos, 0.85f * flick, new Color((byte)255, (byte)140, (byte)40, (byte)110));
        SoftSprite.Draw(pos, 0.45f * flick, new Color((byte)255, (byte)210, (byte)110, (byte)200));
        SoftSprite.Draw(pos, 0.2f, new Color((byte)255, (byte)255, (byte)230, (byte)255));
        SoftSprite.End();
    }

    private void DrawEnemy(Enemy e, Vector3 center, float time, CharacterRig.Fx fx)
    {
        bool trapped = e.State == EnemyState.Trapped;

        _rig.DrawEnemy(e, center, time, _shadowPass, fx);

        if (e.HasGold && _rig.TryGetCarry(out var carry, out float carryYaw))
        {
            Mat(SurfaceMaterial.Gold);
            DrawIngot(carry + new Vector3(0f, -0.07f, 0f), 0.42f, carryYaw + MathF.Sin(time * 3f + e.Id) * 8f);
            Mat(SurfaceMaterial.Clay);
        }
    }

    /// <summary>
    /// Red glow around the monks' eyes plus a faint dark-red aura. Additive and unlit: call after the lighting shader
    /// has ended, inside Mode3D, once per frame after <see cref="Draw"/>.
    /// </summary>
    public void DrawEyeGlows(float time)
    {
        if (_rig.EyeGlows.Count == 0) return;
        SoftSprite.BeginAdditive();
        foreach (var p in _rig.EyeGlows)
        {
            float flick = 0.9f + 0.1f * MathF.Sin(time * 13f + p.X * 7f);
            SoftSprite.Draw(p, 0.20f * flick, new Color((byte)255, (byte)30, (byte)20, (byte)120));
            SoftSprite.Draw(p, 0.075f, new Color((byte)255, (byte)150, (byte)110, (byte)230));
        }
        SoftSprite.End();
        _rig.EyeGlows.Clear();
    }
}
