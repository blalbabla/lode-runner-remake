using System.Numerics;
using LodeRunner.Core.Simulation;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// Procedural chubby clay-like character rig (Human Fall Flat aesthetic).
/// Draws rounded ellipsoid body parts with vertex normals for soft clay shading,
/// distance-driven stride cycles, damped spring yaw/lean/squash, and iconic character accents.
/// </summary>
public sealed partial class CharacterRig
{
    public readonly record struct Palette(
        Color Shirt,
        Color Pants,
        Color Skin,
        Color Boots,
        Color Belt,
        Color Hair,
        Color Accent,
        bool Hat,
        bool Monk = false,
        bool Hero = false);

    /// <summary>
    /// Hero: Shirt = sand tunic and scarf, Pants = dusty trousers, Skin = bare arms and face, Boots = tall boots,
    /// Belt = leather straps, Hair = brown hair, Accent = wrist wraps.
    /// </summary>
    public static readonly Palette PlayerPalette = new(
        Shirt: new Color(226, 212, 180, 255),
        Pants: new Color(204, 190, 160, 255),
        Skin: new Color(232, 190, 150, 255),
        Boots: new Color(118, 88, 58, 255),
        Belt: new Color(88, 58, 38, 255),
        Hair: new Color(96, 64, 40, 255),
        Accent: new Color(238, 230, 208, 255),
        Hat: false,
        Hero: true);

    /// <summary>
    /// Hooded monk: Shirt = cloak/hood cloth, Skin = pale bone hands, Hair = the black void inside the hood,
    /// Boots = feet peeking from under the hem, Accent = eye glow.
    /// </summary>
    public static readonly Palette MonkPalette = new(
        Shirt: new Color(68, 56, 80, 255),
        Pants: new Color(38, 31, 46, 255),
        Skin: new Color(188, 192, 172, 255),
        Boots: new Color(26, 22, 28, 255),
        Belt: new Color(120, 100, 70, 255),
        Hair: new Color(6, 5, 9, 255),
        Accent: new Color(255, 40, 28, 255),
        Hat: false,
        Monk: true);

    /// <summary>
    /// Death-scene modifiers set by <see cref="DeathScene"/>: Jitter = violent shaking, Flail = arms thrown up and waved
    /// frantically, Crush = body flattened into a pancake, Offset = extra world-space shift of the body.
    /// </summary>
    public readonly record struct Fx(float Jitter = 0f, float Flail = 0f, float Crush = 0f, Vector3 Offset = default);

    private sealed class AnimState
    {
        public Fx Fx;
        public Vector3 LastPos;
        public float LastTime;
        public float StridePhase;
        public float ClimbPhase;
        public float Yaw;       // radians: 0 = facing camera (+Z), +pi/2 = facing +X, -pi/2 = facing -X
        public float Lean;      // forward/side pitch in radians
        public float Squash = 1f; // Y scale factor (squash < 1, stretch > 1)
        public float HeadBob;
        public float Billow;    // 0..1: cloak hem streams behind a moving monk
        public float Flare;     // 0..1: cloak balloons upward while falling
        public bool WasFalling;
        public bool Initialized;
    }

    private readonly Dictionary<int, AnimState> _states = new();

    // Unit sphere template built once on CPU so every ellipsoid is emitted with smooth transformed normals.
    private const int Rings = 12;
    private const int Slices = 16;
    private static readonly Vector3[] UnitVerts;
    private static readonly float[] SinTheta;
    private static readonly float[] CosTheta;

    static CharacterRig()
    {
        UnitVerts = new Vector3[(Rings + 1) * (Slices + 1)];
        SinTheta = new float[Rings + 1];
        CosTheta = new float[Rings + 1];
        for (int r = 0; r <= Rings; r++)
        {
            float v = r / (float)Rings;
            float phi = v * MathF.PI;
            float sp = MathF.Sin(phi);
            float cp = MathF.Cos(phi);
            SinTheta[r] = sp;
            CosTheta[r] = cp;
            for (int s = 0; s <= Slices; s++)
            {
                float u = s / (float)Slices;
                float theta = u * MathF.PI * 2f;
                UnitVerts[r * (Slices + 1) + s] = new Vector3(
                    sp * MathF.Sin(theta),
                    cp,
                    sp * MathF.Cos(theta));
            }
        }
    }

    public void DrawPlayer(Player p, Vector3 center, float time, bool shadowPass, Fx fx = default)
    {
        float dir = p.Facing == Facing.Right ? 1f : -1f;
        DrawCharacter(
            id: -1,
            center: center,
            palette: PlayerPalette,
            pose: p.Pose,
            dir: dir,
            moving: p.Moved,
            falling: p.Falling,
            trapped: false,
            climbingOut: false,
            time: time,
            shadowPass: shadowPass,
            fx: fx);
    }

    public void DrawEnemy(Enemy e, Vector3 center, float time, bool shadowPass, Fx fx = default)
    {
        float dir = e.Facing == Facing.Right ? 1f : -1f;
        bool trapped = e.State == EnemyState.Trapped;
        bool climbingOut = e.State == EnemyState.ClimbingOut;
        bool moving = e.Moved && e.State == EnemyState.Active;
        Pose pose = climbingOut ? Pose.Climb : trapped ? Pose.Stand : e.Pose;

        DrawCharacter(
            id: e.Id,
            center: center,
            palette: MonkPalette,
            pose: pose,
            dir: dir,
            moving: moving || climbingOut,
            falling: e.Falling,
            trapped: trapped,
            climbingOut: climbingOut,
            time: time,
            shadowPass: shadowPass,
            fx: fx);
    }

    private void DrawCharacter(
        int id,
        Vector3 center,
        Palette palette,
        Pose pose,
        float dir,
        bool moving,
        bool falling,
        bool trapped,
        bool climbingOut,
        float time,
        bool shadowPass,
        Fx fx)
    {
        var st = GetOrUpdateState(id, center, pose, dir, moving, falling, trapped, climbingOut, time, shadowPass);
        st.Fx = fx;

        bool front = pose is Pose.Climb or Pose.Hang or Pose.Fall || trapped || climbingOut;
        bool dig = pose == Pose.Dig;

        float sy = st.Squash;
        float sxz = 1f / MathF.Sqrt(Math.Max(0.5f, sy));
        float crushDrop = 0f;
        if (fx.Crush > 0f)
        {
            // Pancake: much lower and wider; the body is pushed down so the flattened feet stay on the floor.
            float k = 1f - 0.82f * fx.Crush;
            crushDrop = 0.41f * (1f - k) * sy;
            sy *= k;
            sxz *= 1f + 0.75f * fx.Crush;
        }

        // Base root at hip height: feet rest cleanly at y = center.Y - 0.50 (floor top).
        float s = MathF.Sin(st.StridePhase);
        float c = MathF.Cos(st.StridePhase);
        float cs = MathF.Sin(st.ClimbPhase);
        float cc = MathF.Cos(st.ClimbPhase);

        // A monk glides rather than bounces.
        float runBounce = pose == Pose.Run && moving ? MathF.Abs(s) * (palette.Monk ? 0.018f : 0.060f) : 0f;
        float hangBounce = pose == Pose.Hang ? (moving ? MathF.Abs(s) * 0.032f : MathF.Sin(time * 2.5f + id) * 0.012f) - 0.14f : 0f;
        float climbSway = (pose == Pose.Climb || climbingOut) ? cs * 0.025f : (pose == Pose.Hang && moving ? s * 0.035f : 0f);
        float idleBreath = MathF.Sin(time * 3.2f + id * 1.7f) * 0.010f;
        // Trapped guard hangs by its hands from the pit rim: the body sags under its own weight (arms stretched),
        // with a slow strained sway.
        float trapLift = trapped ? 0.07f + MathF.Sin(time * 5f + id * 1.3f) * 0.012f : 0f;
        float trapSway = trapped ? MathF.Sin(time * 3.1f + id) * 0.012f : 0f;
        var root = center + new Vector3(climbSway + trapSway, -0.09f + runBounce + hangBounce + trapLift - crushDrop, 0.06f)
            + fx.Offset;

        // Build character local-to-world rotation: yaw around Y, lean around X, plus pendulum swing roll on rope.
        float hangSwingRoll = pose == Pose.Hang
            ? (moving ? s * 0.16f - dir * 0.08f : MathF.Sin(time * 2.4f + id) * 0.05f)
            : 0f;
        if (fx.Jitter > 0f)
        {
            // Violent convulsions: fast shaking of the whole body.
            float j = fx.Jitter;
            root += new Vector3(MathF.Sin(time * 131f + id * 1.7f) * 0.035f * j, MathF.Sin(time * 149f + id) * 0.022f * j, 0f);
            hangSwingRoll += MathF.Sin(time * 97f + id * 2.1f) * 0.24f * j;
        }
        var rotYaw = Matrix4x4.CreateRotationY(st.Yaw);
        var rotLean = Matrix4x4.CreateRotationX(st.Lean);
        var rotRoll = Matrix4x4.CreateRotationZ(hangSwingRoll);
        var scaleSquash = Matrix4x4.CreateScale(sxz, sy, sxz);
        var rootXform = scaleSquash * rotLean * rotRoll * rotYaw * Matrix4x4.CreateTranslation(root);

        if (palette.Monk)
        {
            DrawMonk(st, id, pose, dir, moving, trapped, climbingOut, time, idleBreath, s, c, cs, cc,
                center, rootXform, palette, shadowPass);
            return;
        }

        // 1. Sleeker clay torso (waist + chest + belt).
        var bellyLocal = new Vector3(0f, 0.10f + idleBreath * 0.5f, 0.01f);
        var chestLocal = new Vector3(0f, 0.25f + idleBreath, 0f);
        DrawEllipsoid(bellyLocal, new Vector3(0.145f, 0.140f, 0.125f), Quaternion.Identity, rootXform, palette.Shirt);
        DrawEllipsoid(chestLocal, new Vector3(0.140f, 0.135f, 0.118f), Quaternion.Identity, rootXform, palette.Shirt);

        // Belt ring & buckle.
        DrawEllipsoid(new Vector3(0f, 0.020f, 0.005f), new Vector3(0.150f, 0.036f, 0.132f), Quaternion.Identity, rootXform, palette.Belt);
        DrawEllipsoid(new Vector3(0f, 0.020f, 0.135f), new Vector3(0.042f, 0.034f, 0.020f), Quaternion.Identity, rootXform, new Color(255, 238, 150, 255));

        // Pelvis / shorts base.
        DrawEllipsoid(new Vector3(0f, -0.025f, 0f), new Vector3(0.138f, 0.082f, 0.120f), Quaternion.Identity, rootXform, palette.Pants);

        if (palette.Hero) DrawHeroOutfit(rootXform, st, time, id, idleBreath, palette);

        // 2. Rounded head + jiggle.
        float headPitch = dig ? 0.28f : pose == Pose.Fall ? -0.22f : trapped ? 0.22f : MathF.Sin(st.StridePhase * 2f) * (moving ? 0.06f : 0f);
        float headRoll = MathF.Sin(time * 4f + id) * (trapped ? 0.18f : 0.03f);
        var headRot = Quaternion.CreateFromYawPitchRoll(0f, headPitch, headRoll);
        var headCenter = new Vector3(0f, 0.50f + idleBreath * 1.2f + st.HeadBob, 0.01f);
        var headXform = Matrix4x4.CreateFromQuaternion(headRot) * Matrix4x4.CreateTranslation(headCenter) * rootXform;

        DrawEllipsoid(Vector3.Zero, new Vector3(0.160f, 0.155f, 0.158f), Quaternion.Identity, headXform, palette.Skin);

        // Cheeks / nose nub (soft clay charm).
        DrawEllipsoid(new Vector3(0f, -0.012f, 0.152f), new Vector3(0.036f, 0.030f, 0.034f), Quaternion.Identity, headXform,
            Tint(palette.Skin, 0.94f));

        // Bead eyes.
        Color eyeCol = new(22, 22, 28, 255);
        DrawEllipsoid(new Vector3(-0.058f, 0.020f, 0.140f), new Vector3(0.026f, 0.032f, 0.020f), Quaternion.Identity, headXform, eyeCol);
        DrawEllipsoid(new Vector3(0.058f, 0.020f, 0.140f), new Vector3(0.026f, 0.032f, 0.020f), Quaternion.Identity, headXform, eyeCol);

        // Tiny eye highlight beads (skip in shadow pass).
        if (!shadowPass)
        {
            Color glint = new(250, 250, 255, 255);
            DrawEllipsoid(new Vector3(-0.051f, 0.030f, 0.155f), new Vector3(0.009f, 0.010f, 0.008f), Quaternion.Identity, headXform, glint);
            DrawEllipsoid(new Vector3(0.065f, 0.030f, 0.155f), new Vector3(0.009f, 0.010f, 0.008f), Quaternion.Identity, headXform, glint);
        }

        // Headgear / Hair.
        if (palette.Hat)
        {
            // Guard cap: rounded dome + band + front visor brim.
            DrawEllipsoid(new Vector3(0f, 0.080f, -0.005f), new Vector3(0.168f, 0.105f, 0.168f), Quaternion.Identity, headXform, palette.Hair);
            DrawEllipsoid(new Vector3(0f, 0.050f, 0f), new Vector3(0.172f, 0.030f, 0.172f), Quaternion.Identity, headXform, palette.Shirt);
            DrawEllipsoid(new Vector3(0f, 0.044f, 0.128f), new Vector3(0.145f, 0.022f, 0.110f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.18f), headXform, palette.Hair);
            // Gold badge on cap front.
            DrawEllipsoid(new Vector3(0f, 0.088f, 0.165f), new Vector3(0.030f, 0.030f, 0.015f), Quaternion.Identity, headXform, palette.Accent);
        }
        else if (palette.Hero)
        {
            DrawHeroHair(headXform, st, time, id, palette);
        }
        else
        {
            // Runner hair puff + red headband + trailing headband tails.
            DrawEllipsoid(new Vector3(0f, 0.088f, -0.01f), new Vector3(0.165f, 0.100f, 0.165f), Quaternion.Identity, headXform, palette.Hair);
            DrawEllipsoid(new Vector3(0f, 0.048f, 0f), new Vector3(0.170f, 0.034f, 0.168f), Quaternion.Identity, headXform, palette.Accent);
            // Knot & fluttering tail at the back of the head.
            float flutter = MathF.Sin(time * 14f + st.StridePhase) * 0.18f;
            DrawEllipsoid(new Vector3(0f, 0.045f, -0.165f), new Vector3(0.036f, 0.036f, 0.036f), Quaternion.Identity, headXform, palette.Accent);
            DrawEllipsoid(new Vector3(0.025f, 0.015f, -0.215f), new Vector3(0.024f, 0.068f, 0.020f),
                Quaternion.CreateFromYawPitchRoll(0.2f, -0.6f + flutter, 0f), headXform, palette.Accent);
        }

        // 3. Two-segment chubby legs (thigh + calf + rounded boot).
        if (trapped)
        {
            // Legs dangle and kick weakly; hands grip the top edges of the neighbouring bricks.
            float kick = MathF.Sin(time * 3.2f + id) * 0.7f;
            DrawLegs(Pose.Hang, true, false, true, 0f, kick, 0f, 0f, rootXform, palette);
            for (int side = -1; side <= 1; side += 2)
                DrawGrippingArm(side, center, time, id, rootXform, palette);
            return;
        }

        DrawLegs(pose, moving, dig, front, dir, s, c, cs, rootXform, palette);

        // 4. Two-segment chubby arms (upper arm + forearm + round mitten hand).
        DrawArms(pose, moving, dig, climbingOut, dir, s, c, cs, cc, time, rootXform, palette, fx.Flail);
    }

    private static void DrawLegs(
        Pose pose,
        bool moving,
        bool dig,
        bool front,
        float dir,
        float s,
        float c,
        float cs,
        Matrix4x4 rootXform,
        Palette palette)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            // side = -1 (right leg in local, near camera when facing +X), +1 (left leg in local, far when facing +X)
            float phaseSign = side;
            float hipPitch = 0f;
            float kneePitch = 0f;
            float hipSplay = side * 0.06f;

            if (pose == Pose.Fall)
            {
                hipPitch = -0.35f + side * 0.18f;
                kneePitch = 0.65f;
                hipSplay = side * 0.22f;
            }
            else if (pose == Pose.Hang)
            {
                // Dangling legs swinging with momentum as hands alternate overhead.
                float swing = side * s;
                hipPitch = -0.18f + swing * 0.22f;
                kneePitch = 0.42f + MathF.Max(0f, -swing) * 0.35f;
                hipSplay = side * 0.14f + (moving ? s * 0.14f - dir * 0.10f : 0f);
            }
            else if (pose == Pose.Climb || front)
            {
                // Contralateral rung stepping locked to the 4-rungs-per-tile ladder grid (0.25 unit spacing):
                // While a foot is in its stance half-cycle, its height tracks the fixed rung (-0.125..+0.125 offset)
                // as the torso rises, then lifts over to the next rung (0.25 unit span).
                float lift = (-side * cs + 1f) * 0.5f; // [0..1]: 0 = bottom of stroke, 1 = top of stroke (+0.25u higher)
                hipPitch = -(0.08f + 0.94f * lift);    // ΔY between lift=0 and lift=1 is ~0.25 world units (1 rung)
                kneePitch = 0.12f + 1.12f * lift;
                hipSplay = side * 0.11f;
            }
            else if (dig)
            {
                hipPitch = side < 0 ? -0.42f : 0.25f;
                kneePitch = side < 0 ? 0.50f : 0.25f;
            }
            else if (pose == Pose.Run && moving)
            {
                float cyc = phaseSign * s;
                float lift = MathF.Max(0f, phaseSign * c);
                hipPitch = -cyc * 0.92f - lift * 0.26f;
                kneePitch = 0.18f + lift * 1.05f + MathF.Max(0f, cyc) * 0.30f;
            }
            else
            {
                hipPitch = side * 0.06f;
                kneePitch = 0.08f;
            }

            Color pantCol = side > 0 ? Tint(palette.Pants, 0.85f) : palette.Pants;
            Color bootCol = side > 0 ? Tint(palette.Boots, 0.85f) : palette.Boots;

            var hipPos = new Vector3(side * 0.082f, -0.035f, 0f);
            var thighRot = Quaternion.CreateFromYawPitchRoll(0f, hipPitch, hipSplay);
            var hipMat = Matrix4x4.CreateFromQuaternion(thighRot) * Matrix4x4.CreateTranslation(hipPos) * rootXform;

            const float thighLen = 0.165f;
            const float calfLen = 0.160f;

            // Thigh ellipsoid.
            DrawEllipsoid(new Vector3(0f, -thighLen * 0.5f, 0f), new Vector3(0.060f, thighLen * 0.62f, 0.064f),
                Quaternion.Identity, hipMat, pantCol);

            // Knee & calf.
            var kneeMat = Matrix4x4.CreateRotationX(kneePitch) * Matrix4x4.CreateTranslation(new Vector3(0f, -thighLen, 0f)) * hipMat;
            DrawEllipsoid(new Vector3(0f, -calfLen * 0.5f, 0f), new Vector3(0.052f, calfLen * 0.62f, 0.056f),
                Quaternion.Identity, kneeMat, pantCol);
            if (palette.Hero) DrawHeroLegGear(hipMat, kneeMat, thighLen, calfLen, palette);

            // Boot at ankle.
            var ankleMat = Matrix4x4.CreateTranslation(new Vector3(0f, -calfLen, 0.032f)) * kneeMat;
            DrawEllipsoid(Vector3.Zero, new Vector3(0.062f, 0.048f, 0.096f), Quaternion.Identity, ankleMat, bootCol);
        }
    }

    /// <summary>
    /// Arm reaching up and out to the top edge of the brick beside the pit (two-bone IK in the character's
    /// frontal plane). Limb length stretches slightly when the rim is out of natural reach, like a hanging weight.
    /// </summary>
    private static void DrawGrippingArm(int sideHint, Vector3 center, float time, int id, Matrix4x4 rootXform, Palette palette)
    {
        const float upperLen = 0.160f;
        const float foreLen = 0.150f;

        var shoulderLocal = new Vector3(sideHint * 0.155f, 0.31f, 0f);
        // Pick the rim side by where this shoulder really is in the world, whatever the current body yaw.
        var shoulderWorld = Vector3.Transform(shoulderLocal, rootXform);
        float side = shoulderWorld.X >= center.X ? 1f : -1f;

        float strain = MathF.Sin(time * 6f + id + sideHint * 1.7f) * 0.012f;
        var worldTarget = new Vector3(center.X + side * 0.47f, center.Y + 0.52f + strain, center.Z + 0.10f);
        if (!Matrix4x4.Invert(rootXform, out var inv)) return;
        var target = Vector3.Transform(worldTarget, inv);

        var d = target - shoulderLocal;
        d.Z = 0f;
        float dist = MathF.Max(0.01f, d.Length());
        float stretch = MathF.Max(1f, dist / ((upperLen + foreLen) * 0.98f));
        float A = upperLen * stretch;
        float B = foreLen * stretch;

        float cosA = Math.Clamp((A * A + dist * dist - B * B) / (2f * A * dist), -1f, 1f);
        float thetaT = MathF.Atan2(d.X, -d.Y);
        float sgn = d.X >= 0f ? 1f : -1f;
        float thetaU = thetaT - sgn * MathF.Acos(cosA); // elbow below the shoulder-hand line
        var elbow = shoulderLocal + new Vector3(MathF.Sin(thetaU), -MathF.Cos(thetaU), 0f) * A;
        var fv = target - elbow;
        float thetaF = MathF.Atan2(fv.X, -fv.Y);

        Color sleeveCol = sideHint > 0 ? Tint(palette.Shirt, 0.85f) : palette.Shirt;
        Color skinCol = sideHint > 0 ? Tint(palette.Skin, 0.88f) : palette.Skin;

        var shoulderMat = Matrix4x4.CreateRotationZ(thetaU) * Matrix4x4.CreateTranslation(shoulderLocal) * rootXform;
        float k = palette.Monk ? 1.5f : 1f;
        DrawEllipsoid(new Vector3(0f, -A * 0.5f, 0f), new Vector3(0.048f * k, A * 0.62f, 0.048f * k),
            Quaternion.Identity, shoulderMat, sleeveCol);

        var elbowMat = Matrix4x4.CreateRotationZ(thetaF - thetaU) * Matrix4x4.CreateTranslation(new Vector3(0f, -A, 0f)) * shoulderMat;
        DrawEllipsoid(new Vector3(0f, -B * 0.5f, 0f), new Vector3(0.042f * k, B * 0.62f, 0.042f * k),
            Quaternion.Identity, elbowMat, palette.Monk ? sleeveCol : skinCol);

        var wristMat = Matrix4x4.CreateTranslation(new Vector3(0f, -B, 0f)) * elbowMat;
        DrawHand(wristMat, skinCol, palette.Monk, 0.056f);
    }

    /// <summary>Round mitten for the clay characters; long bony hand for the monk.</summary>
    private static void DrawHand(Matrix4x4 wristMat, Color skinCol, bool monk, float round)
    {
        if (!monk)
        {
            DrawEllipsoid(Vector3.Zero, new Vector3(round, round * 0.93f, round), Quaternion.Identity, wristMat, skinCol);
            return;
        }
        DrawEllipsoid(new Vector3(0f, -0.045f, 0f), new Vector3(0.034f, 0.062f, 0.026f), Quaternion.Identity, wristMat, skinCol);
        DrawEllipsoid(new Vector3(0.020f, -0.095f, 0.006f), new Vector3(0.010f, 0.040f, 0.010f), Quaternion.Identity, wristMat, Tint(skinCol, 0.9f));
        DrawEllipsoid(new Vector3(-0.020f, -0.090f, 0.006f), new Vector3(0.010f, 0.036f, 0.010f), Quaternion.Identity, wristMat, Tint(skinCol, 0.9f));
    }

    private static void DrawArms(
        Pose pose,
        bool moving,
        bool dig,
        bool frantic,
        float dir,
        float s,
        float c,
        float cs,
        float cc,
        float time,
        Matrix4x4 rootXform,
        Palette palette,
        float flail = 0f)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            float phaseSign = -side; // opposite to same-side leg
            float shoulderPitch;
            float shoulderRoll = side * 0.18f;
            float shoulderYaw = 0f;
            float elbowPitch;

            if (flail > 0.01f)
            {
                // Death throes: both arms thrown overhead and waved wildly.
                float w1 = MathF.Sin(time * 31f + side * 2.1f);
                float w2 = MathF.Sin(time * 37f + side * 0.7f);
                shoulderPitch = -2.55f + w1 * 0.55f * flail;
                shoulderRoll = side * (0.30f + 0.50f * w2 * flail);
                shoulderYaw = -side * 0.25f * w1 * flail;
                elbowPitch = -0.45f - 0.55f * (0.5f + 0.5f * w2) * flail;
            }
            else if (dig)
            {
                float pulse = MathF.Sin(time * 22f + side) * 0.18f;
                shoulderPitch = -1.15f + pulse;
                shoulderYaw = -side * 0.28f;
                shoulderRoll = side * 0.08f;
                elbowPitch = -0.55f;
            }
            else if (pose == Pose.Hang)
            {
                // Hand-over-hand rope traversal (left-right-left-right):
                // One hand reaches out along the rope in movement direction while the other pulls & releases.
                float cyc = side * s;   // [-1..1] alternating left (side=+1) and right (side=-1)
                float arc = side * c;   // > 0 when hand is swinging over to grab, < 0 when pulling body across
                float grip = (cyc + 1f) * 0.5f; // [0..1]
                shoulderPitch = moving
                    ? -(2.45f + 0.50f * grip - MathF.Max(0f, arc) * 0.28f)
                    : -2.80f;
                // Lateral reach along the rope: when facing -Z (Yaw = pi), local +X is world -X.
                shoulderRoll = moving
                    ? side * 0.10f - dir * cyc * 0.42f
                    : side * 0.14f;
                shoulderYaw = moving ? -dir * cyc * 0.22f : 0f;
                elbowPitch = moving
                    ? -(0.65f - 0.45f * grip + MathF.Max(0f, arc) * 0.25f)
                    : -0.28f;
            }
            else if (pose == Pose.Climb || frantic)
            {
                // Opposite to same-side leg, matched to the 0.25-unit ladder rung spacing:
                // lift = 0 holds the rung at chest level (+0.32u), lift = 1 grips the next rung above (+0.57u).
                float lift = (side * cs + 1f) * 0.5f; // [0..1]
                shoulderPitch = -(1.42f + 1.28f * lift); // ΔY of hand between lift=0 and lift=1 is ~0.25 world units (1 rung)
                shoulderRoll = side * (0.18f - 0.10f * lift);
                shoulderYaw = -side * 0.14f;
                elbowPitch = -(0.95f - 0.65f * lift);
            }
            else if (pose == Pose.Fall)
            {
                float wave = MathF.Sin(time * 18f + side * 1.5f) * 0.25f;
                shoulderPitch = -2.45f + wave;
                shoulderRoll = side * 0.42f;
                elbowPitch = -0.35f;
            }
            else if (pose == Pose.Run && moving)
            {
                float swing = phaseSign * s;
                shoulderPitch = -swing * 1.05f;
                shoulderRoll = side * 0.18f;
                elbowPitch = -0.45f - MathF.Max(0f, swing) * 0.55f;
            }
            else
            {
                shoulderPitch = 0.08f * side;
                shoulderRoll = side * 0.16f;
                elbowPitch = -0.25f;
            }

            Color sleeveCol = side > 0 ? Tint(palette.Shirt, 0.85f) : palette.Shirt;
            Color skinCol = side > 0 ? Tint(palette.Skin, 0.88f) : palette.Skin;
            if (palette.Hero) sleeveCol = skinCol; // sleeveless tunic: bare arms

            var shoulderPos = new Vector3(side * 0.155f, 0.31f, 0f);
            var upperRot = Quaternion.CreateFromYawPitchRoll(shoulderYaw, shoulderPitch, shoulderRoll);
            var shoulderMat = Matrix4x4.CreateFromQuaternion(upperRot) * Matrix4x4.CreateTranslation(shoulderPos) * rootXform;

            const float upperLen = 0.160f;
            const float foreLen = 0.150f;

            // Upper arm (shirt sleeve).
            float k = palette.Monk ? 1.5f : 1f;
            DrawEllipsoid(new Vector3(0f, -upperLen * 0.5f, 0f), new Vector3(0.048f * k, upperLen * 0.62f, 0.048f * k),
                Quaternion.Identity, shoulderMat, sleeveCol);

            // Forearm (a monk's wide sleeve covers it down to the wrist).
            var elbowMat = Matrix4x4.CreateRotationX(elbowPitch) * Matrix4x4.CreateTranslation(new Vector3(0f, -upperLen, 0f)) * shoulderMat;
            DrawEllipsoid(new Vector3(0f, -foreLen * 0.5f, 0f), new Vector3(0.042f * k, foreLen * 0.62f, 0.042f * k),
                Quaternion.Identity, elbowMat, palette.Monk ? sleeveCol : skinCol);

            // Bandage wrap around the wrist.
            if (palette.Hero)
            {
                DrawEllipsoid(new Vector3(0f, -foreLen * 0.80f, 0f), new Vector3(0.050f, 0.036f, 0.050f), Quaternion.Identity, elbowMat, palette.Accent);
                DrawEllipsoid(new Vector3(0f, -foreLen * 0.62f, 0f), new Vector3(0.047f, 0.014f, 0.047f), Quaternion.Identity, elbowMat, Tint(palette.Accent, 0.88f));
            }

            // Hand.
            var wristMat = Matrix4x4.CreateTranslation(new Vector3(0f, -foreLen, 0f)) * elbowMat;
            DrawHand(wristMat, skinCol, palette.Monk, 0.052f);
        }
    }

    private AnimState GetOrUpdateState(
        int id,
        Vector3 center,
        Pose pose,
        float dir,
        bool moving,
        bool falling,
        bool trapped,
        bool climbingOut,
        float time,
        bool shadowPass)
    {
        if (!_states.TryGetValue(id, out var st))
        {
            st = new AnimState();
            _states[id] = st;
        }

        // Advance simulation only once per frame (or if shadowPass is the first pass of this timestamp).
        float dt = time - st.LastTime;
        if (!st.Initialized || dt < -0.5f || Vector3.DistanceSquared(center, st.LastPos) > 4f)
        {
            st.Initialized = true;
            st.LastPos = center;
            st.LastTime = time;
            st.Yaw = TargetYaw(pose, dir, trapped, climbingOut);
            st.Lean = 0f;
            st.Squash = 1f;
            st.WasFalling = falling || pose == Pose.Fall;
            return st;
        }

        if (dt <= 1e-5f)
            return st;

        dt = Math.Min(dt, 0.1f);
        var delta = center - st.LastPos;
        st.LastPos = center;
        st.LastTime = time;

        // Distance-driven stride & climb cycles matched to physical limb length (~0.52 unit per step, ~1.05 per full cycle)
        // so planted feet lock to the ground without sliding or frantic pedaling.
        float dx = MathF.Abs(delta.X);
        float dy = MathF.Abs(delta.Y);
        if (pose == Pose.Run && moving)
        {
            float stepAdvance = dx * 5.2f;
            st.StridePhase = (st.StridePhase + stepAdvance) % (MathF.PI * 2f);
        }
        else if (pose == Pose.Hang && moving)
        {
            float hangAdvance = dx * 5.0f;
            st.StridePhase = (st.StridePhase + hangAdvance) % (MathF.PI * 2f);
        }
        else
        {
            // Smoothly relax stride phase toward neutral stance.
            st.StridePhase = DampAngle(st.StridePhase, 0f, 10f, dt);
        }

        if (pose == Pose.Climb || climbingOut)
        {
            // Ladder has 4 rungs per tile (0.25 world units per rung) at y = k*0.25 + 0.125.
            // One full left+right cycle (2*pi radians) covers 2 rungs (0.50 world units),
            // so each 0.25-unit rung step is exactly pi radians (12.5664 rad per world unit).
            // Locking phase directly to world Y aligns hand/foot placements with the actual rung heights.
            if ( dy > 1e-4f || !moving )
            {
                float targetClimbPhase = (center.Y - 0.125f) * (MathF.PI * 4f);
                st.ClimbPhase = DampAngle(st.ClimbPhase, targetClimbPhase, 28f, dt);
            }
            else if (moving && dx > 1e-4f)
            {
                st.ClimbPhase = (st.ClimbPhase + dx * (MathF.PI * 4f)) % (MathF.PI * 2f);
            }
        }
        else if (trapped)
        {
            st.ClimbPhase = (st.ClimbPhase + dt * 6f) % (MathF.PI * 2f);
        }

        // Squash & stretch on falling / landing.
        bool nowFalling = falling || pose == Pose.Fall;
        if (nowFalling)
        {
            st.Squash = Damp(st.Squash, 1.16f, 12f, dt);
        }
        else
        {
            if (st.WasFalling)
            {
                st.Squash = 0.78f; // impact squash on landing
                st.HeadBob = -0.045f;
            }
            st.Squash = Damp(st.Squash, 1f, 14f, dt);
        }
        st.WasFalling = nowFalling;
        st.HeadBob = Damp(st.HeadBob, 0f, 16f, dt);

        // Smooth yaw toward look direction.
        float targetYaw = TargetYaw(pose, dir, trapped, climbingOut);
        st.Yaw = DampAngle(st.Yaw, targetYaw, 16f, dt);

        // Forward lean when running or digging.
        float targetLean = pose == Pose.Dig ? 0.26f : (pose == Pose.Run && moving ? 0.18f : 0f);
        st.Lean = Damp(st.Lean, targetLean, 14f, dt);

        st.Billow = Damp(st.Billow, pose == Pose.Run && moving ? 1f : 0f, 5f, dt);
        st.Flare = Damp(st.Flare, nowFalling ? 1f : 0f, 7f, dt);

        return st;
    }

    private static float TargetYaw(Pose pose, float dir, bool trapped, bool climbingOut)
    {
        if (pose is Pose.Climb or Pose.Hang || climbingOut)
            return dir * MathF.PI; // Face ladder/rope (-Z, back to camera); sign matches dir for shortest turn
        if (trapped)
            return 0f; // facing the camera, hanging in the pit
        if (pose == Pose.Fall)
            return dir * 0.35f;
        // Side poses (Stand, Run, Dig): 3/4 view angled toward camera (+Z) so both eyes, belly, and limbs read clearly.
        return dir * (MathF.PI * 0.32f);
    }

    private static float Damp(float current, float target, float lambda, float dt)
        => Vector2.Lerp(new Vector2(current, 0f), new Vector2(target, 0f), 1f - MathF.Exp(-lambda * dt)).X;

    private static float DampAngle(float current, float target, float lambda, float dt)
    {
        float diff = MathF.IEEERemainder(target - current, MathF.PI * 2f);
        return current + diff * (1f - MathF.Exp(-lambda * dt));
    }

    private static Color Tint(Color c, float factor)
    {
        return new Color(
            (byte)Math.Clamp((int)(c.R * factor), 0, 255),
            (byte)Math.Clamp((int)(c.G * factor), 0, 255),
            (byte)Math.Clamp((int)(c.B * factor), 0, 255),
            c.A);
    }

    /// <summary>
    /// Emits a smooth ellipsoid into Rlgl's immediate batch with true world-space vertex normals.
    /// Works identically in both the shadow depth pass and the main lighting pass without breaking shadow map bindings.
    /// </summary>
    private static void DrawEllipsoid(Vector3 localPos, Vector3 radii, Quaternion localRot, Matrix4x4 parentTransform, Color color)
    {
        var model = Matrix4x4.CreateScale(radii)
            * Matrix4x4.CreateFromQuaternion(localRot)
            * Matrix4x4.CreateTranslation(localPos)
            * parentTransform;

        // Normal matrix is inverse-transpose of the upper 3x3; for scale(radii) * rotation, invScale * rotation is exact.
        var invRadii = new Vector3(
            1f / Math.Max(1e-4f, radii.X),
            1f / Math.Max(1e-4f, radii.Y),
            1f / Math.Max(1e-4f, radii.Z));
        var normalMat = Matrix4x4.CreateScale(invRadii)
            * Matrix4x4.CreateFromQuaternion(localRot)
            * ClearTranslation(parentTransform);

        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(color.R, color.G, color.B, color.A);

        int stride = Slices + 1;
        for (int r = 0; r < Rings; r++)
        {
            int row0 = r * stride;
            int row1 = (r + 1) * stride;
            for (int s = 0; s < Slices; s++)
            {
                var u00 = UnitVerts[row0 + s];
                var u10 = UnitVerts[row1 + s];
                var u11 = UnitVerts[row1 + s + 1];
                var u01 = UnitVerts[row0 + s + 1];

                EmitVertex(u00, ref model, ref normalMat);
                EmitVertex(u10, ref model, ref normalMat);
                EmitVertex(u11, ref model, ref normalMat);

                EmitVertex(u00, ref model, ref normalMat);
                EmitVertex(u11, ref model, ref normalMat);
                EmitVertex(u01, ref model, ref normalMat);
            }
        }

        Rlgl.End();
    }

    private static void EmitVertex(Vector3 unit, ref Matrix4x4 model, ref Matrix4x4 normalMat)
    {
        var n = Vector3.Normalize(Vector3.TransformNormal(unit, normalMat));
        var p = Vector3.Transform(unit, model);
        Rlgl.Normal3f(n.X, n.Y, n.Z);
        Rlgl.Vertex3f(p.X, p.Y, p.Z);
    }

    private static Matrix4x4 ClearTranslation(Matrix4x4 m)
    {
        m.M41 = 0f;
        m.M42 = 0f;
        m.M43 = 0f;
        m.M44 = 1f;
        return m;
    }
}
