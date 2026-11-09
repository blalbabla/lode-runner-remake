using System.Numerics;
using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>Surface type the fragment shader shades procedurally. Matches the <c>material</c> uniform.</summary>
public enum SurfaceMaterial
{
    /// <summary>Flat colour with Lambert lighting (characters, backdrop, rope).</summary>
    Plain = 0,
    /// <summary>Procedural brick wall with bump-mapped mortar, grain and moss.</summary>
    Brick = 1,
    /// <summary>Bevelled steel plate with rivets and brushed metal (indestructible blocks).</summary>
    Steel = 2,
    /// <summary>Polished metal for round parts such as ladders.</summary>
    Metal = 3,
    /// <summary>Gold with warm reflections and a faint self-glow.</summary>
    Gold = 4,
    /// <summary>Large light bricks of the background wall, bump-mapped.</summary>
    Wall = 5,
    /// <summary>Smooth matte clay with vertex normals, half-Lambert wrap diffuse, soft specular, and rim light.</summary>
    Clay = 6,
}

/// <summary>
/// One directional light plus ambient for Raylib's immediate-mode primitives, with procedural materials.
/// Immediate mode carries no normals, so the fragment shader rebuilds each face normal from screen-space
/// derivatives of the world position (flat shading), then perturbs it with procedural height maps (bump mapping)
/// for brick, steel and gold. Lines have no derivative area and stay unlit.
/// If the shader cannot be built, <see cref="IsAvailable"/> is false and drawing is left unlit.
/// </summary>
public sealed class LightingShader : IDisposable
{
    public const int MaxGlowLights = 16;

    private const string Vertex = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec3 fragPos;
        out vec2 fragTexCoord;
        out vec3 fragNormal;
        out vec4 fragColor;
        void main()
        {
            fragPos = vertexPosition;
            fragTexCoord = vertexTexCoord;
            fragNormal = vertexNormal;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string Fragment = """
        #version 330
        in vec3 fragPos;
        in vec2 fragTexCoord;
        in vec3 fragNormal;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 lightDir;   // unit vector pointing toward the light
        uniform float ambient;
        uniform float diffuse;
        uniform vec3 viewPos;
        uniform int material;    // see SurfaceMaterial
        uniform int detail;      // 0 = cheap flat look, 1 = bump mapping and noise
        uniform int glowCount;
        uniform vec3 glowPos[16];
        uniform sampler2D shadowMap;
        uniform mat4 lightVP;
        uniform int shadows;     // 1 = sample the shadow map
        uniform float shadowTexel;
        out vec4 finalColor;

        // 1.0 = lit, 0.0 = fully in shadow. PCF over a 5x5 kernel for soft edges.
        float shadowFactor(vec3 n)
        {
            if (shadows == 0) return 1.0;
            float ndl = dot(n, normalize(lightDir));
            if (ndl <= 0.0) return 1.0; // faces turned away are already dark
            vec4 lp = lightVP * vec4(fragPos + n * 0.03, 1.0);
            vec3 pc = lp.xyz / lp.w * 0.5 + 0.5;
            if (pc.x < 0.0 || pc.x > 1.0 || pc.y < 0.0 || pc.y > 1.0 || pc.z > 1.0) return 1.0;
            float bias = 0.0004 + 0.0012 * (1.0 - ndl);
            float lit = 0.0;
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                {
                    float d = texture(shadowMap, pc.xy + vec2(x, y) * shadowTexel * 1.2).r;
                    lit += pc.z - bias > d ? 0.0 : 1.0;
                }
            return lit / 25.0;
        }

        float hash21(vec2 p)
        {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        float vnoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash21(i);
            float b = hash21(i + vec2(1.0, 0.0));
            float c = hash21(i + vec2(0.0, 1.0));
            float d = hash21(i + vec2(1.0, 1.0));
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }

        float fbm(vec2 p)
        {
            float s = 0.0;
            float a = 0.5;
            for (int i = 0; i < 4; i++)
            {
                s += a * vnoise(p);
                p = p * 2.03 + vec2(17.1, 9.7);
                a *= 0.5;
            }
            return s;
        }

        // Screen-space bump mapping: h is a height in world units, so the result is a perturbed normal.
        vec3 bump(vec3 n, float h)
        {
            vec3 dpx = dFdx(fragPos);
            vec3 dpy = dFdy(fragPos);
            float dhx = dFdx(h);
            float dhy = dFdy(h);
            vec3 r1 = cross(dpy, n);
            vec3 r2 = cross(n, dpx);
            float det = dot(dpx, r1);
            vec3 grad = sign(det) * (dhx * r1 + dhy * r2);
            return normalize(abs(det) * n - grad);
        }

        // 2D coordinates on the face: front (x,y), side (z,y), top (x,z).
        vec2 faceUv(vec3 n)
        {
            vec3 a = abs(n);
            if (a.z >= a.x && a.z >= a.y) return fragPos.xy;
            if (a.x >= a.y) return fragPos.zy;
            return fragPos.xz;
        }

        void brickSurface(vec2 uv, out float h, out vec3 col, out float spec)
        {
            const vec2 bs = vec2(0.5, 0.25);
            float row = floor(uv.y / bs.y);
            float off = mod(row, 2.0) * 0.25;
            float qx = uv.x + off;
            float colId = floor(qx / bs.x);
            vec2 id = vec2(colId, row);
            vec2 l = vec2(qx - colId * bs.x, uv.y - row * bs.y);
            float e = min(min(l.x, bs.x - l.x), min(l.y, bs.y - l.y));

            float grain = detail > 0 ? fbm(uv * 10.0) : 0.5;
            float pits = detail > 0 ? vnoise(uv * 28.0) : 0.5;
            e += detail > 0 ? (vnoise(uv * 14.0) - 0.5) * 0.014 : 0.0;
            float brick = smoothstep(0.010, 0.050, e);

            float r = hash21(id + 3.7);
            float r2 = hash21(id + 11.3);
            vec3 b = mix(vec3(0.34, 0.11, 0.08), vec3(0.70, 0.30, 0.19), r * 0.7 + r * r * 0.3);
            if (r2 > 0.86) b = mix(b, vec3(0.80, 0.40, 0.21), 0.6);
            if (r2 < 0.10) b *= 0.7;
            b *= 0.70 + 0.60 * grain;
            b *= 0.82 + 0.36 * vnoise(uv * 2.6 + 5.0);
            b *= mix(0.60, 1.0, smoothstep(0.010, 0.10, e));

            float moss = smoothstep(0.60, 0.80, vnoise(uv * 24.0 + 9.0));
            float nearMortar = 1.0 - smoothstep(0.015, 0.09, e);
            b = mix(b, vec3(0.20, 0.26, 0.09), moss * 0.35 * nearMortar);

            vec3 mortar = vec3(0.15, 0.12, 0.09) * (0.7 + 0.6 * vnoise(uv * 30.0));
            mortar = mix(mortar, vec3(0.20, 0.25, 0.08), moss * 0.8);

            col = mix(mortar, b, brick);
            h = brick * (0.024 + 0.014 * grain + 0.004 * pits);
            spec = brick;
        }

        // Backdrop: lighter, weathered wall of large bricks with deep mortar joints, stains and faint moss.
        void wallSurface(vec2 uv, out float h, out vec3 col, out float spec)
        {
            const vec2 bs = vec2(1.0, 0.5);
            float row = floor(uv.y / bs.y);
            float off = mod(row, 2.0) * 0.5;
            float qx = uv.x + off;
            float colId = floor(qx / bs.x);
            vec2 id = vec2(colId, row) + 41.0;
            vec2 l = vec2(qx - colId * bs.x, uv.y - row * bs.y);
            float e = min(min(l.x, bs.x - l.x), min(l.y, bs.y - l.y));

            float grain = detail > 0 ? fbm(uv * 6.0) : 0.5;
            float pits = detail > 0 ? vnoise(uv * 22.0) : 0.5;
            e += detail > 0 ? (vnoise(uv * 9.0) - 0.5) * 0.03 : 0.0;
            float brick = smoothstep(0.020, 0.090, e);

            float r = hash21(id + 3.7);
            float r2 = hash21(id + 11.3);
            vec3 b = mix(vec3(0.38, 0.28, 0.23), vec3(0.55, 0.41, 0.33), r * 0.7 + r * r * 0.3);
            if (r2 > 0.85) b = mix(b, vec3(0.58, 0.45, 0.34), 0.5);
            if (r2 < 0.10) b *= 0.88;
            b *= 0.88 + 0.24 * grain;
            b *= 0.93 + 0.14 * vnoise(uv * 1.3 + 5.0);
            b *= mix(0.78, 1.0, smoothstep(0.020, 0.18, e));

            // Damp stains running down the wall and moss crawling out of the joints.
            float stain = smoothstep(0.55, 0.85, vnoise(vec2(uv.x * 0.9, uv.y * 0.18) + 3.1));
            b = mix(b, b * vec3(0.62, 0.60, 0.58), stain * 0.30);
            float moss = smoothstep(0.62, 0.82, vnoise(uv * 14.0 + 9.0));
            float nearMortar = 1.0 - smoothstep(0.03, 0.17, e);
            b = mix(b, vec3(0.30, 0.38, 0.17), moss * 0.25 * nearMortar);

            vec3 mortar = vec3(0.34, 0.31, 0.27) * (0.82 + 0.36 * vnoise(uv * 20.0));
            mortar = mix(mortar, vec3(0.30, 0.38, 0.16), moss * 0.7);

            col = mix(mortar, b, brick);
            h = brick * (0.040 + 0.020 * grain + 0.006 * pits);
            spec = brick;
        }

        vec3 shadeMetal(vec3 N, vec3 c, float envAmt, float shininess, vec3 specTint, float sh)
        {
            vec3 L = normalize(lightDir);
            vec3 V = normalize(viewPos - fragPos);
            vec3 H = normalize(L + V);
            float ndl = max(dot(N, L), 0.0);
            vec3 R = reflect(-V, N);
            float k = smoothstep(-0.35, 0.55, R.y);
            vec3 env = mix(vec3(0.10, 0.12, 0.17), vec3(0.85, 0.90, 1.0), k);
            env += vec3(1.0, 0.95, 0.85) * pow(max(dot(R, L), 0.0), 10.0) * 0.6;
            float spec = pow(max(dot(N, H), 0.0), shininess);
            vec3 diff = c * (ambient + diffuse * ndl * sh) * 0.45;
            return diff + env * c * envAmt * mix(0.55, 1.0, sh) + specTint * spec * 0.8 * sh;
        }

        // Warm light from nearby gold: returns an additive factor to multiply with the albedo.
        vec3 glowLight(vec3 N)
        {
            vec3 g = vec3(0.0);
            for (int i = 0; i < glowCount; i++)
            {
                vec3 d = glowPos[i] - fragPos;
                float dist2 = dot(d, d);
                float att = 1.0 / (1.0 + dist2 * 7.0);
                float nd = 0.35 + 0.65 * max(dot(N, d * inversesqrt(dist2 + 1e-5)), 0.0);
                g += vec3(1.0, 0.72, 0.28) * att * nd * 0.6;
            }
            return g;
        }

        void main()
        {
            vec4 base = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
            vec3 n;
            float vnLen = length(fragNormal);
            if (material == 6 && vnLen > 1e-4)
            {
                n = fragNormal / vnLen;
            }
            else
            {
                n = cross(dFdx(fragPos), dFdy(fragPos));
                float len = length(n);
                if (len <= 1e-7)
                {
                    finalColor = base;
                    return;
                }
                n /= len;
            }

            vec3 L = normalize(lightDir);
            vec3 albedo = base.rgb;
            vec3 col;
            vec3 N = n;
            float sh = shadowFactor(n);

            if (material == 1)
            {
                float h;
                float sp;
                brickSurface(faceUv(n), h, albedo, sp);
                if (detail > 0) N = bump(n, h);
                vec3 V = normalize(viewPos - fragPos);
                vec3 H = normalize(L + V);
                float s = pow(max(dot(N, H), 0.0), 28.0) * 0.10 * sp;
                col = albedo * (ambient + diffuse * max(dot(N, L), 0.0) * sh) + vec3(1.0, 0.9, 0.8) * s * sh;
            }
            else if (material == 5)
            {
                float h;
                float sp;
                wallSurface(faceUv(n), h, albedo, sp);
                if (detail > 0) N = bump(n, h);
                vec3 V = normalize(viewPos - fragPos);
                vec3 H = normalize(L + V);
                float s = pow(max(dot(N, H), 0.0), 24.0) * 0.07 * sp;
                col = albedo * (ambient + diffuse * max(dot(N, L), 0.0) * sh) + vec3(1.0, 0.92, 0.82) * s * sh;
            }
            else if (material == 2)
            {
                float h = 0.0;
                albedo = vec3(0.50, 0.54, 0.62) * (0.92 + 0.16 * hash21(floor(fragPos.xy) + 1.3));
                if (abs(n.z) > 0.7)
                {
                    vec2 p = fract(fragPos.xy) - 0.5;
                    float d = max(abs(p.x), abs(p.y));
                    h += 0.022 * (1.0 - smoothstep(0.44, 0.50, d));
                    h -= 0.016 * (1.0 - smoothstep(0.31, 0.37, d));
                    float r = length(abs(p) - vec2(0.405));
                    h += 0.030 * (1.0 - smoothstep(0.012, 0.052, r));
                    albedo *= mix(1.0, 0.78, 1.0 - smoothstep(0.31, 0.37, d));
                }
                if (detail > 0)
                {
                    h += 0.0012 * vnoise(vec2(fragPos.x * 36.0, fragPos.y * 2.0));
                    h += 0.0010 * vnoise(vec2(fragPos.x * 2.0, fragPos.y * 36.0));
                    N = bump(n, h);
                }
                col = shadeMetal(N, albedo, 0.75, 50.0, vec3(1.0), sh);
            }
            else if (material == 3)
            {
                albedo = vec3(0.74, 0.78, 0.86);
                if (detail > 0) N = bump(n, 0.0015 * vnoise(vec2(fragPos.x * 40.0, fragPos.y * 2.0)));
                col = shadeMetal(N, albedo, 0.70, 80.0, vec3(1.0), sh);
            }
            else if (material == 4)
            {
                albedo = vec3(1.0, 0.76, 0.25);
                if (detail > 0) N = bump(n, 0.004 * vnoise(fragPos.xy * 20.0 + fragPos.z * 13.0));
                col = shadeMetal(N, albedo, 0.95, 60.0, vec3(1.0, 0.88, 0.55), sh) + albedo * 0.10;
            }
            else if (material == 6)
            {
                if (detail > 0)
                {
                    float clayH = 0.0022 * fbm(fragPos.xy * 18.0 + fragPos.z * 11.0);
                    N = bump(n, clayH);
                    albedo *= 0.96 + 0.08 * vnoise(fragPos.xy * 22.0 + fragPos.z * 15.0);
                }
                vec3 V = normalize(viewPos - fragPos);
                vec3 H = normalize(L + V);
                float wrap = clamp((dot(N, L) + 0.35) / 1.35, 0.0, 1.0);
                float softSh = mix(0.42, 1.0, sh);
                float spec = pow(max(dot(N, H), 0.0), 16.0) * 0.18 * sh;
                float rim = pow(1.0 - max(dot(N, V), 0.0), 2.8) * 0.22;
                float hemi = 0.5 + 0.5 * N.y;
                vec3 skyBounce = mix(vec3(0.76, 0.70, 0.66), vec3(1.06, 1.03, 0.98), hemi);
                col = albedo * (ambient * skyBounce + diffuse * wrap * softSh)
                    + vec3(1.0, 0.96, 0.90) * spec
                    + mix(albedo, vec3(1.0, 0.95, 0.88), 0.5) * rim;
            }
            else
            {
                col = albedo * (ambient + diffuse * max(dot(n, L), 0.0) * sh);
            }

            if (material != 4 && detail > 0) col += albedo * glowLight(N);
            finalColor = vec4(col, base.a);
        }
        """;

    private readonly Shader _shader;
    private readonly int _locLightDir, _locAmbient, _locDiffuse, _locViewPos, _locMaterial, _locDetail,
        _locGlowCount, _locGlowPos, _locShadowMap, _locLightVP, _locShadows, _locShadowTexel;
    private ShadowMap? _shadowMap;
    private SurfaceMaterial _current = SurfaceMaterial.Plain;
    private readonly Vector3[] _glow = new Vector3[MaxGlowLights];

    public bool IsAvailable { get; }

    /// <summary>Direction toward the light in world space. Set before the first frame.</summary>
    // Sun behind the player's shoulder: in front of the scene, above and slightly to the left.
    // Front faces (normal +Z) come out at about 1.0 so base colours keep their designed brightness.
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.35f, 0.5f, 0.8f));
    public float Ambient { get; set; } = 0.38f;
    public float Diffuse { get; set; } = 0.78f;

    /// <summary>False selects the cheap look: no bump mapping, no noise, no gold light.</summary>
    public bool HighDetail { get; set; } = true;

    public LightingShader()
    {
        _shader = Raylib.LoadShaderFromMemory(Vertex, Fragment);
        IsAvailable = Raylib.IsShaderValid(_shader);
        if (!IsAvailable) return;
        _locLightDir = Raylib.GetShaderLocation(_shader, "lightDir");
        _locAmbient = Raylib.GetShaderLocation(_shader, "ambient");
        _locDiffuse = Raylib.GetShaderLocation(_shader, "diffuse");
        _locViewPos = Raylib.GetShaderLocation(_shader, "viewPos");
        _locMaterial = Raylib.GetShaderLocation(_shader, "material");
        _locDetail = Raylib.GetShaderLocation(_shader, "detail");
        _locGlowCount = Raylib.GetShaderLocation(_shader, "glowCount");
        _locGlowPos = Raylib.GetShaderLocation(_shader, "glowPos");
        _locShadowMap = Raylib.GetShaderLocation(_shader, "shadowMap");
        _locLightVP = Raylib.GetShaderLocation(_shader, "lightVP");
        _locShadows = Raylib.GetShaderLocation(_shader, "shadows");
        _locShadowTexel = Raylib.GetShaderLocation(_shader, "shadowTexel");
    }

    /// <summary>Shadow map rendered this frame, or null to draw without shadows.</summary>
    public void UseShadows(ShadowMap? map) => _shadowMap = map is { IsValid: true } ? map : null;

    // Raylib's batch may rebind texture slots on every flush, so bind the depth texture again after each one.
    private void BindShadowTexture()
    {
        if (_shadowMap is null) return;
        Raylib.SetShaderValueTexture(_shader, _locShadowMap,
            new Texture2D { Id = _shadowMap.DepthTextureId, Width = _shadowMap.Size, Height = _shadowMap.Size, Mipmaps = 1 });
    }

    /// <param name="viewPos">Camera position, used for specular and environment reflections.</param>
    public void Begin(Vector3 viewPos)
    {
        if (!IsAvailable) return;
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValue(_shader, _locLightDir, Vector3.Normalize(LightDirection), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locAmbient, Ambient, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locDiffuse, Diffuse, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locViewPos, viewPos, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locDetail, HighDetail ? 1 : 0, ShaderUniformDataType.Int);
        _current = SurfaceMaterial.Plain;
        Raylib.SetShaderValue(_shader, _locMaterial, (int)_current, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _locShadows, _shadowMap is null ? 0 : 1, ShaderUniformDataType.Int);
        if (_shadowMap is not null)
        {
            Raylib.SetShaderValueMatrix(_shader, _locLightVP, _shadowMap.LightViewProjection);
            Raylib.SetShaderValue(_shader, _locShadowTexel, 1f / _shadowMap.Size, ShaderUniformDataType.Float);
            BindShadowTexture();
        }
    }

    /// <summary>
    /// Selects the material for everything drawn next. Pending geometry is flushed first,
    /// otherwise it would be drawn with the new material.
    /// </summary>
    public void SetMaterial(SurfaceMaterial material)
    {
        if (!IsAvailable || material == _current) return;
        Rlgl.DrawRenderBatchActive();
        _current = material;
        Raylib.SetShaderValue(_shader, _locMaterial, (int)material, ShaderUniformDataType.Int);
        BindShadowTexture();
    }

    /// <summary>Positions of gold pieces that cast a warm light on nearby surfaces (at most <see cref="MaxGlowLights"/>).</summary>
    public void SetGlowLights(ReadOnlySpan<Vector3> positions)
    {
        if (!IsAvailable) return;
        Rlgl.DrawRenderBatchActive();
        int n = Math.Min(positions.Length, MaxGlowLights);
        positions[..n].CopyTo(_glow);
        Raylib.SetShaderValue(_shader, _locGlowCount, n, ShaderUniformDataType.Int);
        if (n > 0) Raylib.SetShaderValueV(_shader, _locGlowPos, _glow, ShaderUniformDataType.Vec3, MaxGlowLights);
        BindShadowTexture();
    }

    public void End()
    {
        if (IsAvailable) Raylib.EndShaderMode();
    }

    public void Dispose()
    {
        if (IsAvailable) Raylib.UnloadShader(_shader);
    }
}
