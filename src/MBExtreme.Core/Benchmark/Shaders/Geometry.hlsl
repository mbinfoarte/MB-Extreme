// Cena 2 · Geometria: 2.500 esferas deformadas (5,8 milhões de triângulos por quadro) com 3 luzes que andam pela cena.
#define SLICES 48
#define STACKS 24
#define GRID 50

struct GeoOut { float4 pos : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 tint : TEXCOORD2; };

float3 CamEye() { float a = time * 0.12; return float3(24.0 * cos(a), 6.5 + 1.5 * sin(time * 0.2), 24.0 * sin(a)); }

float Bump(float3 d, float seed)
{
    return 1.0 + 0.16 * sin(d.x * 6.0 + time * 1.7 + seed) * sin(d.y * 5.0 + seed * 1.3) + 0.08 * sin(d.z * 9.0 - time * 2.1 + seed * 0.7);
}

float3 SpherePoint(float u, float v, float seed)
{
    float theta = v * 3.14159265; float phi = u * 6.2831853;
    float3 d = float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi));
    return d * Bump(d, seed);
}

GeoOut VSGeometry(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float2 corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
    uint quad = vid / 6; float2 c = corners[vid % 6];
    float u = ((quad % SLICES) + c.x) / SLICES;
    float v = ((quad / SLICES) + c.y) / STACKS;
    float seed = inst * 0.618;
    float3 local = SpherePoint(u, v, seed);
    // normal pela diferença com dois pontos vizinhos
    float3 du = SpherePoint(u + 0.004, v, seed) - local;
    float3 dv = SpherePoint(u, min(v + 0.004, 0.999), seed) - local;
    float3 n = normalize(cross(dv, du));
    if (dot(n, local) < 0.0) n = -n;
    float2 cell = float2(inst % GRID, inst / GRID) - (GRID - 1) * 0.5;
    float scale = 0.55 + 0.45 * Hash21(cell);
    float3 center = float3(cell.x * 2.4, 1.2 + 0.8 * sin(cell.x * 0.35 + time) * cos(cell.y * 0.3 + time * 0.8), cell.y * 2.4);
    GeoOut o;
    o.world = center + local * scale;
    o.normal = n;
    float pick = Hash21(cell + 7.0);
    o.tint = pick < 0.35 ? float3(1.0, 0.76, 0.1) : pick < 0.55 ? float3(1.0, 0.5, 0.15) : pick < 0.8 ? float3(0.55, 0.52, 0.62) : float3(0.75, 0.6, 1.0);
    o.pos = Project(o.world, CamEye(), float3(0, 1.0, 0), 1.5);
    return o;
}

float3 PointLight(float3 p, float3 n, float3 lp, float3 lc)
{
    float3 l = lp - p; float d = length(l); l /= d;
    return lc * saturate(dot(n, l)) * 40.0 / (1.0 + d * d * 0.06);
}

float4 PSGeometry(GeoOut i) : SV_Target
{
    float3 n = normalize(i.normal);
    float3 l1 = float3(18.0 * cos(time * 0.5), 6.0, 18.0 * sin(time * 0.5));
    float3 l2 = float3(-14.0 * cos(time * 0.37), 5.0, 10.0 * sin(time * 0.61));
    float3 l3 = float3(8.0 * sin(time * 0.8), 7.0, -16.0 * cos(time * 0.43));
    float3 light = PointLight(i.world, n, l1, float3(1.0, 0.75, 0.2)) + PointLight(i.world, n, l2, float3(0.6, 0.45, 1.0)) + PointLight(i.world, n, l3, float3(1.0, 0.45, 0.25));
    float3 eye = CamEye();
    float3 v = normalize(eye - i.world);
    float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
    float3 col = i.tint * (0.12 + 0.5 * saturate(n.y * 0.5 + 0.5) * 0.3 + light) + rim * float3(0.5, 0.45, 0.6);
    float fog = 1.0 - exp(-length(eye - i.world) * 0.018);
    col = lerp(col, float3(0.07, 0.07, 0.09), fog);
    return float4(col / (1.0 + col * 0.6), 1.0);
}

float4 PSBackdrop(VSOut i) : SV_Target
{
    float t = i.pos.y / res.y;
    float3 top = float3(0.05, 0.05, 0.07), horizon = float3(0.13, 0.12, 0.16), bottom = float3(0.06, 0.06, 0.07);
    float3 c = t < 0.45 ? lerp(top, horizon, t / 0.45) : lerp(horizon, bottom, (t - 0.45) / 0.55);
    return float4(c, 1.0);
}
