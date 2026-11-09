using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>
/// The hero: a wandering treasure hunter in a sand-coloured sleeveless tunic with a torn, asymmetric hem, bare arms
/// with wrist wraps, a diagonal leather strap carrying a satchel, thigh straps, tall laced boots, a shoulder scarf and
/// shoulder-length brown hair. Cloth is a surface of revolution, like the monk's cloak.
/// </summary>
public sealed partial class CharacterRig
{
    private const int SkirtRings = 6;

    /// <summary>Everything the hero wears on the body: tunic skirt, scarf, strap, satchel and the second belt.</summary>
    private static void DrawHeroOutfit(Matrix4x4 rootXform, AnimState st, float time, int id, float idleBreath, Palette palette)
    {
        DrawTunicSkirt(rootXform, st, time, id, palette.Shirt);

        // Second belt higher on the waist, with a worn brass buckle.
        DrawEllipsoid(new Vector3(0f, 0.075f, 0.004f), new Vector3(0.150f, 0.024f, 0.130f), Quaternion.Identity, rootXform, Tint(palette.Belt, 1.12f));
        DrawEllipsoid(new Vector3(0.03f, 0.075f, 0.132f), new Vector3(0.020f, 0.020f, 0.010f), Quaternion.Identity, rootXform, new Color(176, 148, 84, 255));

        // Diagonal strap from the right shoulder to the satchel on the left hip.
        const int links = 16;
        for (int i = 0; i <= links; i++)
        {
            float t = i / (float)links;
            float x = -0.115f + 0.270f * t;
            float y = 0.350f + idleBreath - 0.355f * t;
            float z = 0.118f * MathF.Sqrt(MathF.Max(0.10f, 1f - (x / 0.145f) * (x / 0.145f))) + 0.004f;
            DrawEllipsoid(new Vector3(x, y, z), new Vector3(0.024f, 0.030f, 0.010f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.75f), rootXform, palette.Belt);
        }

        // Satchel: body, flap and buckle.
        float bagSwing = st.Billow * MathF.Sin(time * 9f + id) * 0.02f;
        var bag = new Vector3(0.190f, -0.040f + bagSwing, -0.005f);
        Color leather = Tint(palette.Belt, 1.35f);
        DrawEllipsoid(bag, new Vector3(0.050f, 0.072f, 0.088f), Quaternion.Identity, rootXform, leather);
        DrawEllipsoid(bag + new Vector3(0.004f, 0.038f, 0f), new Vector3(0.054f, 0.042f, 0.092f), Quaternion.Identity, rootXform, Tint(leather, 0.82f));
        DrawEllipsoid(bag + new Vector3(0.050f, 0.020f, 0f), new Vector3(0.008f, 0.014f, 0.012f), Quaternion.Identity, rootXform, new Color(176, 148, 84, 255));

        // Scarf: a wrap across the shoulders with one end hanging over the chest.
        Color scarf = Tint(palette.Shirt, 1.06f);
        DrawEllipsoid(new Vector3(0f, 0.325f + idleBreath, -0.005f), new Vector3(0.185f, 0.040f, 0.140f), Quaternion.Identity, rootXform, scarf);
        DrawEllipsoid(new Vector3(-0.060f, 0.265f + idleBreath, 0.108f), new Vector3(0.042f, 0.095f, 0.024f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.28f), rootXform, Tint(scarf, 0.93f));
        float tail = MathF.Sin(time * 7f + id) * 0.25f * st.Billow;
        DrawEllipsoid(new Vector3(0.060f, 0.315f + idleBreath, -0.130f), new Vector3(0.030f, 0.090f, 0.022f),
            Quaternion.CreateFromYawPitchRoll(0f, 0.55f + tail, 0.2f), rootXform, Tint(scarf, 0.9f));
    }

    /// <summary>Shoulder-length hair with a heavy back mass, side locks and a stubbled jaw.</summary>
    private static void DrawHeroHair(Matrix4x4 headXform, AnimState st, float time, int id, Palette palette)
    {
        Color hair = palette.Hair;
        float stream = st.Billow * (0.35f + 0.1f * MathF.Sin(time * 11f + id));

        DrawEllipsoid(new Vector3(0f, 0.085f, -0.012f), new Vector3(0.168f, 0.102f, 0.166f), Quaternion.Identity, headXform, hair);
        // Back mass falling to the shoulders (streams behind when running).
        DrawEllipsoid(new Vector3(0f, -0.045f, -0.125f), new Vector3(0.150f, 0.185f, 0.085f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.22f + stream), headXform, Tint(hair, 0.92f));
        // Side locks framing the face, with a swept fringe.
        DrawEllipsoid(new Vector3(-0.150f, -0.025f, 0.010f), new Vector3(0.034f, 0.125f, 0.075f), Quaternion.Identity, headXform, hair);
        DrawEllipsoid(new Vector3(0.150f, -0.025f, 0.010f), new Vector3(0.034f, 0.125f, 0.075f), Quaternion.Identity, headXform, hair);
        DrawEllipsoid(new Vector3(0.030f, 0.105f, 0.115f), new Vector3(0.120f, 0.042f, 0.060f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.20f), headXform, Tint(hair, 1.08f));

        // Stubble: slightly darker lower face.
        DrawEllipsoid(new Vector3(0f, -0.078f, 0.085f), new Vector3(0.100f, 0.062f, 0.085f), Quaternion.Identity, headXform, Tint(palette.Skin, 0.80f));
    }

    /// <summary>Tall laced boot shaft and a leather strap around the thigh.</summary>
    private static void DrawHeroLegGear(Matrix4x4 hipMat, Matrix4x4 kneeMat, float thighLen, float calfLen, Palette palette)
    {
        Color leather = Tint(palette.Belt, 1.15f);
        DrawEllipsoid(new Vector3(0f, -thighLen * 0.62f, 0f), new Vector3(0.070f, 0.016f, 0.074f), Quaternion.Identity, hipMat, leather);
        // Boot shaft up the calf, with lace bands.
        DrawEllipsoid(new Vector3(0f, -calfLen * 0.68f, 0f), new Vector3(0.062f, calfLen * 0.36f, 0.066f), Quaternion.Identity, kneeMat, palette.Boots);
        for (int i = 0; i < 3; i++)
            DrawEllipsoid(new Vector3(0f, -calfLen * (0.50f + 0.17f * i), 0.012f), new Vector3(0.066f, 0.011f, 0.070f),
                Quaternion.Identity, kneeMat, Tint(leather, 0.8f));
    }

    /// <summary>Ragged tunic skirt: longer on the sides and back, torn along the hem, swaying and streaming in motion.</summary>
    private static void DrawTunicSkirt(Matrix4x4 rootXform, AnimState st, float time, int id, Color cloth)
    {
        const int stride = CloakSlices + 1;
        float seed = id * 1.3f;

        for (int i = 0; i <= SkirtRings; i++)
        {
            float t = i / (float)SkirtRings;
            for (int j = 0; j <= CloakSlices; j++)
            {
                float th = j / (float)CloakSlices * MathF.PI * 2f;
                float sn = MathF.Sin(th);
                float cn = MathF.Cos(th);

                // Hem height: short in front, long on the left side and back; jagged tears along it.
                float hem = -0.15f - 0.075f * sn + 0.05f * cn
                            + 0.024f * MathF.Sin(9f * th + 0.4f) + 0.014f * MathF.Sin(14f * th + 1.1f);
                float y = 0.075f + (hem - 0.075f) * t;

                float fold = 0.06f * MathF.Sin(5f * th + 0.7f) + 0.03f * MathF.Sin(8f * th + 2.1f);
                float sway = MathF.Sin(3f * th - time * 2.4f + seed) * 0.010f * t;
                float r = 0.150f + 0.045f * t + fold * t * 0.55f + sway;

                // Falling: the hem balloons up; running: it streams backwards.
                y += st.Flare * 0.16f * t * t;
                r *= 1f + st.Flare * 0.30f * t;
                float z = r * cn * 0.90f;
                z -= st.Billow * t * t * (0.07f + 0.03f * MathF.Sin(time * 10f + seed + th)) * (0.5f + 0.5f * (1f - cn) * 0.5f);

                int idx = i * stride + j;
                CloakPos[idx] = Vector3.Transform(new Vector3(r * sn, y, z), rootXform);
                CloakShade[idx] = (0.90f + 0.10f * (fold / 0.09f) * t) * (1f - 0.16f * t * t);
            }
        }

        FinishCloth(SkirtRings, cloth);
    }
}
