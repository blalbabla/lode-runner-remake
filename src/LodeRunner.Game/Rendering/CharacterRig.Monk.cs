using System.Numerics;
using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// The enemy "monk": a gaunt figure in a long hooded cloak. The cloak is a smooth surface of revolution (rings of
/// vertices with folds, a swaying hem, wind billow while running and a flare while falling) emitted straight into
/// Rlgl's immediate batch with real normals, so it goes through the same lighting and shadow paths as the ellipsoids.
/// </summary>
public sealed partial class CharacterRig
{
    private const int CloakRings = 16;
    private const int CloakSlices = 24;

    // Cloak silhouette from the neck down to the hem: (local y, radius). Local y = 0 is the hip, the floor is about -0.41; the hem stops short of it so the feet show.
    private static readonly (float Y, float R)[] CloakProfile =
    [
        (0.40f, 0.060f),
        (0.36f, 0.140f),
        (0.31f, 0.205f),
        (0.22f, 0.200f),
        (0.10f, 0.195f),
        (-0.05f, 0.215f),
        (-0.18f, 0.245f),
        (-0.27f, 0.272f),
        (-0.335f, 0.288f),
    ];

    private static readonly Vector3[] CloakPos = new Vector3[(CloakRings + 1) * (CloakSlices + 1)];
    private static readonly Vector3[] CloakNrm = new Vector3[(CloakRings + 1) * (CloakSlices + 1)];
    private static readonly float[] CloakShade = new float[(CloakRings + 1) * (CloakSlices + 1)];

    private bool _hasCarry;
    private Vector3 _carryPos;
    private float _carryYawDeg;

    /// <summary>World positions of the monks' eyes drawn this frame, for the unlit glow pass.</summary>
    internal readonly List<Vector3> EyeGlows = [];

    /// <summary>Where the gold ingot held by the monk just drawn should go (bottom centre) and how it is yawed.</summary>
    internal bool TryGetCarry(out Vector3 pos, out float yawDeg)
    {
        pos = _carryPos;
        yawDeg = _carryYawDeg;
        return _hasCarry;
    }

    private void DrawMonk(
        AnimState st,
        int id,
        Pose pose,
        float dir,
        bool moving,
        bool trapped,
        bool climbingOut,
        float time,
        float idleBreath,
        float s,
        float c,
        float cs,
        float cc,
        Vector3 center,
        Matrix4x4 rootXform,
        Palette palette,
        bool shadowPass)
    {
        bool front = pose is Pose.Climb or Pose.Hang or Pose.Fall || trapped || climbingOut;
        Color cloth = palette.Shirt;

        // Legs first (mostly hidden by the cloak; they only show as feet under the hem).
        if (trapped)
        {
            float kick = MathF.Sin(time * 3.2f + id) * 0.7f;
            DrawLegs(Pose.Hang, true, false, true, 0f, kick, 0f, 0f, rootXform, palette);
        }
        else
        {
            DrawLegs(pose, moving, false, front, dir, s * 0.55f, c * 0.55f, cs, rootXform, palette);
        }

        DrawCloak(rootXform, st, time, id, cloth);

        // Shoulder mantle rounds off the top of the cloak.
        DrawEllipsoid(new Vector3(0f, 0.30f + idleBreath, 0f), new Vector3(0.218f, 0.092f, 0.178f),
            Quaternion.Identity, rootXform, Tint(cloth, 1.08f));

        // Rope belt with a hanging end.
        DrawEllipsoid(new Vector3(0f, 0.045f, 0f), new Vector3(0.212f, 0.022f, 0.206f),
            Quaternion.Identity, rootXform, palette.Belt);
        DrawEllipsoid(new Vector3(0.075f, -0.040f, 0.200f), new Vector3(0.014f, 0.085f, 0.014f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.10f), rootXform, palette.Belt);

        // ---- hood ----
        float headPitch = 0.10f + (pose == Pose.Fall ? -0.32f : trapped ? 0.14f : MathF.Sin(st.StridePhase * 2f) * (moving ? 0.05f : 0f));
        float headRoll = MathF.Sin(time * 4f + id) * (trapped ? 0.18f : 0.02f);
        var headRot = Quaternion.CreateFromYawPitchRoll(0f, headPitch, headRoll);
        var headCenter = new Vector3(0f, 0.49f + idleBreath * 1.2f + st.HeadBob, 0.035f);
        var headXform = Matrix4x4.CreateFromQuaternion(headRot) * Matrix4x4.CreateTranslation(headCenter) * rootXform;

        Color hood = Tint(cloth, 0.94f);
        Color rim = Tint(cloth, 1.12f);
        DrawEllipsoid(new Vector3(0f, 0.03f, -0.03f), new Vector3(0.190f, 0.200f, 0.190f), Quaternion.Identity, headXform, hood);
        // Pointed tip drooping down the back.
        DrawEllipsoid(new Vector3(0f, 0.16f, -0.17f), new Vector3(0.078f, 0.150f, 0.078f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.95f), headXform, hood);
        // Cheek folds and a heavy brow that shadow the opening.
        DrawEllipsoid(new Vector3(-0.145f, -0.010f, 0.080f), new Vector3(0.065f, 0.150f, 0.120f), Quaternion.Identity, headXform, rim);
        DrawEllipsoid(new Vector3(0.145f, -0.010f, 0.080f), new Vector3(0.065f, 0.150f, 0.120f), Quaternion.Identity, headXform, rim);
        DrawEllipsoid(new Vector3(0f, 0.120f, 0.105f), new Vector3(0.165f, 0.068f, 0.115f), Quaternion.Identity, headXform, rim);

        // The void inside the hood.
        DrawEllipsoid(new Vector3(0f, -0.010f, 0.090f), new Vector3(0.105f, 0.140f, 0.115f), Quaternion.Identity, headXform, palette.Hair);

        // Slanted red eyes.
        float pulse = 1f + 0.12f * MathF.Sin(time * 5f + id * 2.3f);
        for (int side = -1; side <= 1; side += 2)
        {
            var eyeLocal = new Vector3(side * 0.050f, 0.012f, 0.190f);
            DrawEllipsoid(eyeLocal, new Vector3(0.030f * pulse, 0.014f * pulse, 0.012f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, side * 0.42f), headXform, palette.Accent);
            if (!shadowPass)
                EyeGlows.Add(Vector3.Transform(eyeLocal + new Vector3(0f, 0f, 0.02f), headXform));
        }

        // Bone-white grin: a curved row of small teeth in the dark.
        if (!shadowPass)
        {
            Color bone = new(214, 208, 184, 255);
            for (int k = -3; k <= 3; k++)
            {
                float x = k * 0.0165f;
                float curl = x * x * 2.6f;
                float h = (k & 1) == 0 ? 0.016f : 0.011f;
                DrawEllipsoid(new Vector3(x, -0.072f + curl, 0.182f - MathF.Abs(x) * 0.5f),
                    new Vector3(0.0075f, h, 0.006f), Quaternion.Identity, headXform, bone);
            }
        }

        // ---- arms ----
        if (trapped)
        {
            for (int side = -1; side <= 1; side += 2)
                DrawGrippingArm(side, center, time, id, rootXform, palette);
        }
        else
        {
            DrawArms(pose, moving, false, climbingOut, dir, s, c, cs, cc, time, rootXform, palette, st.Fx.Flail);
        }

        // Where a carried ingot is clutched: against the chest, in front of the cloak.
        _hasCarry = true;
        _carryPos = Vector3.Transform(new Vector3(0f, 0.12f, 0.24f), rootXform);
        _carryYawDeg = st.Yaw * (180f / MathF.PI);
    }

    private static void DrawCloak(Matrix4x4 rootXform, AnimState st, float time, int id, Color cloth)
    {
        const int stride = CloakSlices + 1;
        float seed = id * 1.7f;

        for (int i = 0; i <= CloakRings; i++)
        {
            float t = i / (float)CloakRings;
            SampleCloakProfile(t, out float y, out float r);
            float w = SmoothStep(0.18f, 1f, t);

            for (int j = 0; j <= CloakSlices; j++)
            {
                float th = j / (float)CloakSlices * MathF.PI * 2f;
                float sn = MathF.Sin(th);
                float cn = MathF.Cos(th);

                // Vertical folds, deeper toward the hem; integer multiples of th keep the seam closed.
                float fold = 0.070f * MathF.Sin(6f * th + 0.5f) + 0.032f * MathF.Sin(11f * th + 1.3f);
                float sway = MathF.Sin(3f * th - time * 2.1f + seed) * 0.014f * w * w;
                float rr = r * (1f + fold * w) + sway;
                float yy = y + MathF.Sin(5f * th + time * 1.7f + seed * 2f) * 0.012f * t * t * t;

                // Falling: the hem balloons upward and outward.
                yy += st.Flare * 0.30f * t * t;
                rr *= 1f + st.Flare * 0.35f * t;

                float x = rr * sn;
                float z = rr * cn;

                // Running: the lower cloak streams backwards (local -Z), fuller behind than in front.
                float wind = st.Billow * t * t * (0.10f + 0.04f * MathF.Sin(time * 9f + seed + th));
                z -= wind * (0.6f + 0.4f * MathF.Max(0f, -cn) * 2f);

                int idx = i * stride + j;
                CloakPos[idx] = Vector3.Transform(new Vector3(x, yy, z), rootXform);
                CloakShade[idx] = (0.84f + 0.16f * (fold / 0.102f) * w) * (1f - 0.30f * t * t);
            }
        }

        FinishCloth(CloakRings, cloth);
    }

    /// <summary>
    /// Computes smooth normals for the first <paramref name="rings"/>+1 rings of the shared cloth grid (already in
    /// world space) and emits it as triangles.
    /// </summary>
    private static void FinishCloth(int rings, Color cloth)
    {
        const int stride = CloakSlices + 1;

        // Rings run downward, slices counter-clockwise seen from above, so cross(down, around) points outward.
        for (int i = 0; i <= rings; i++)
        {
            int ip = Math.Max(i - 1, 0);
            int inx = Math.Min(i + 1, rings);
            for (int j = 0; j <= CloakSlices; j++)
            {
                int jp = (j - 1 + CloakSlices) % CloakSlices;
                int jn = (j + 1) % CloakSlices;
                var down = CloakPos[inx * stride + j] - CloakPos[ip * stride + j];
                var around = CloakPos[i * stride + jn] - CloakPos[i * stride + jp];
                var n = Vector3.Cross(down, around);
                CloakNrm[i * stride + j] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
            }
        }

        Rlgl.Begin(DrawMode.Triangles);
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < CloakSlices; j++)
            {
                int a = i * stride + j;
                int b = (i + 1) * stride + j;
                int c = (i + 1) * stride + j + 1;
                int d = i * stride + j + 1;
                EmitCloakVertex(a, cloth);
                EmitCloakVertex(b, cloth);
                EmitCloakVertex(c, cloth);
                EmitCloakVertex(a, cloth);
                EmitCloakVertex(c, cloth);
                EmitCloakVertex(d, cloth);
            }
        }
        Rlgl.End();
    }

    private static void EmitCloakVertex(int idx, Color cloth)
    {
        var col = Tint(cloth, CloakShade[idx]);
        var n = CloakNrm[idx];
        var p = CloakPos[idx];
        Rlgl.Color4ub(col.R, col.G, col.B, col.A);
        Rlgl.Normal3f(n.X, n.Y, n.Z);
        Rlgl.Vertex3f(p.X, p.Y, p.Z);
    }

    private static void SampleCloakProfile(float t, out float y, out float r)
    {
        int last = CloakProfile.Length - 1;
        float u = t * last;
        int i = Math.Min((int)u, last - 1);
        float f = u - i;
        var p0 = CloakProfile[Math.Max(i - 1, 0)];
        var p1 = CloakProfile[i];
        var p2 = CloakProfile[i + 1];
        var p3 = CloakProfile[Math.Min(i + 2, last)];
        y = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, f);
        r = CatmullRom(p0.R, p1.R, p2.R, p3.R, f);
    }

    private static float CatmullRom(float a, float b, float c, float d, float t) =>
        0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (-a + 3f * b - 3f * c + d) * t * t * t);

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
