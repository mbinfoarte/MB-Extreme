// Cena 1 · Reflexos: anel dourado girando, gota amarela que se deforma, esfera cromada e uma floresta de pilares
// sobre um piso xadrez. Tudo por raymarching, com sombra suave, oclusão e um reflexo por superfície.
float SdBox(float3 p, float3 b) { float3 q = abs(p) - b; return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0); }
float SdTorus(float3 p, float2 t) { float2 q = float2(length(p.xz) - t.x, p.y); return length(q) - t.y; }
float SMin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }

float2 Map(float3 p)
{
    float2 r = float2(p.y, 0.0); // piso
    // pilares repetidos, longe do centro
    float2 cell = floor(p.xz / 4.0 + 0.5);
    float2 q = p.xz - cell * 4.0;
    if (length(cell) > 1.2)
    {
        float h = 0.6 + Hash21(cell) * 1.4;
        float d = SdBox(float3(q.x, p.y - h, q.y), float3(0.38, h, 0.38)) - 0.05;
        if (d < r.x) r = float2(d, 1.0);
    }
    // anel em pé girando em volta do eixo vertical
    float a = time * 0.6;
    float3 tp = p - float3(0.0, 1.75, 0.0);
    tp = float3(tp.x * cos(a) - tp.z * sin(a), tp.y, tp.x * sin(a) + tp.z * cos(a));
    float dt = SdTorus(float3(tp.x, tp.z, tp.y), float2(1.45, 0.13));
    if (dt < r.x) r = float2(dt, 2.0);
    // gota: duas esferas que se juntam e se separam
    float s1 = length(p - float3(-0.45 + 0.25 * sin(time * 1.3), 1.7, 0.0)) - 0.48;
    float s2 = length(p - float3(0.45 - 0.25 * sin(time * 1.3), 1.75 + 0.2 * cos(time), 0.1)) - 0.42;
    float db = SMin(s1, s2, 0.35);
    if (db < r.x) r = float2(db, 3.0);
    // esfera cromada
    float dc = length(p - float3(2.6, 0.55, -1.6)) - 0.55;
    if (dc < r.x) r = float2(dc, 4.0);
    return r;
}

float3 Normal(float3 p)
{
    const float2 e = float2(0.0015, -0.0015);
    return normalize(e.xyy * Map(p + e.xyy).x + e.yyx * Map(p + e.yyx).x + e.yxy * Map(p + e.yxy).x + e.xxx * Map(p + e.xxx).x);
}

float2 March(float3 ro, float3 rd, float tmax)
{
    float t = 0.02;
    [loop] for (int i = 0; i < 110; i++)
    {
        float2 h = Map(ro + rd * t);
        if (h.x < 0.0008 * t) return float2(t, h.y);
        t += h.x;
        if (t > tmax) break;
    }
    return float2(-1.0, -1.0);
}

float Shadow(float3 ro, float3 rd)
{
    float res0 = 1.0; float t = 0.03;
    [loop] for (int i = 0; i < 32; i++)
    {
        float h = Map(ro + rd * t).x;
        res0 = min(res0, 10.0 * h / t);
        t += clamp(h, 0.03, 0.5);
        if (res0 < 0.002 || t > 12.0) break;
    }
    return saturate(res0);
}

float Ao(float3 p, float3 n)
{
    float occ = 0.0; float sca = 1.0;
    [unroll] for (int i = 1; i <= 5; i++) { float h = 0.03 * i; occ += (h - Map(p + n * h).x) * sca; sca *= 0.8; }
    return saturate(1.0 - 2.5 * occ);
}

float3 Albedo(float m, float3 p)
{
    if (m < 0.5) { float c = fmod(abs(floor(p.x) + floor(p.z)), 2.0); return lerp(float3(0.85, 0.85, 0.88), float3(0.22, 0.22, 0.26), c); }
    if (m < 1.5) return float3(0.36, 0.34, 0.42);
    if (m < 2.5) return float3(1.0, 0.72, 0.25);
    if (m < 3.5) return float3(1.0, 0.76, 0.05);
    return float3(0.92, 0.93, 0.95);
}

float3 Sky(float3 rd) { return lerp(float3(0.16, 0.15, 0.2), float3(0.06, 0.06, 0.08), saturate(rd.y * 1.5 + 0.2)); }

float3 Shade(float3 p, float3 rd, float m, out float3 n, out float refl)
{
    n = Normal(p);
    float3 light = normalize(float3(0.6, 0.9, 0.4));
    float3 alb = Albedo(m, p);
    float dif = saturate(dot(n, light)) * Shadow(p + n * 0.01, light);
    float spe = pow(saturate(dot(reflect(rd, n), light)), m > 1.5 ? 48.0 : 16.0);
    float occ = Ao(p, n);
    refl = m < 0.5 ? 0.18 : m < 1.5 ? 0.05 : m < 2.5 ? 0.55 : m < 3.5 ? 0.35 : 0.9;
    float3 col = alb * (0.18 * occ + 1.05 * dif) + spe * (m > 1.5 ? 1.2 : 0.3) * dif;
    return col;
}

float4 PSReflections(VSOut i) : SV_Target
{
    float a = time * 0.22 + variation * 2.1;
    float3 eye = float3(6.5 * cos(a), 2.4 + 0.4 * sin(time * 0.3), 6.5 * sin(a));
    float3 ro = eye; float3 rd = CameraRay(i.pos.xy, eye, float3(0.0, 1.2, 0.0), 1.8);
    float3 col = Sky(rd);
    float3 weight = 1.0;
    [loop] for (int bounce = 0; bounce < 2; bounce++)
    {
        float2 h = March(ro, rd, 60.0);
        if (h.x < 0.0) { col = bounce == 0 ? Sky(rd) : col + weight * Sky(rd); break; }
        float3 p = ro + rd * h.x; float3 n; float refl;
        float3 c = Shade(p, rd, h.y, n, refl);
        float fog = 1.0 - exp(-0.0025 * h.x * h.x);
        c = lerp(c, Sky(rd), fog);
        if (bounce == 0) col = c * (1.0 - refl); else col += weight * c;
        weight *= refl * float3(1.0, 0.95, 0.9);
        ro = p + n * 0.01; rd = reflect(rd, n);
    }
    col = col / (1.0 + col);
    col = pow(col, 0.85);
    return float4(col, 1.0);
}
