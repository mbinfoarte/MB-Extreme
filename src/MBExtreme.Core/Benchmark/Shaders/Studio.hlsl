// Cena 3 · Estúdio: path tracing progressivo (a luz rebate até 5 vezes e o ruído some a cada quadro).
Texture2D<float4> Accum : register(t0);
uint Hash(uint s) { s = (s ^ 61u) ^ (s >> 16); s *= 9u; s = s ^ (s >> 4); s *= 0x27d4eb2du; s = s ^ (s >> 15); return s; }
float Rnd(inout uint s) { s = s * 747796405u + 2891336453u; uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u; w = (w >> 22u) ^ w; return w / 4294967296.0; }
float3 RndSphere(inout uint s) { float z = Rnd(s) * 2.0 - 1.0; float a = Rnd(s) * 6.2831853; float r = sqrt(max(0.0, 1.0 - z * z)); return float3(r * cos(a), r * sin(a), z); }
struct Hit { float t; float3 n; int m; };
void Sphere(float3 ro, float3 rd, float3 c, float r, int m, inout Hit h)
{
    float3 oc = ro - c; float b = dot(oc, rd); float k = dot(oc, oc) - r * r; float d = b * b - k;
    if (d < 0.0) return;
    float q = sqrt(d); float t = -b - q; if (t < 0.001) t = -b + q;
    if (t > 0.001 && t < h.t) { h.t = t; h.n = normalize(ro + rd * t - c); h.m = m; }
}
void Box(float3 ro, float3 rd, float3 c, float3 e, float ang, int m, inout Hit h)
{
    float ca = cos(ang), sa = sin(ang);
    float3 o = ro - c; o = float3(ca * o.x + sa * o.z, o.y, -sa * o.x + ca * o.z);
    float3 d = float3(ca * rd.x + sa * rd.z, rd.y, -sa * rd.x + ca * rd.z);
    float3 inv = 1.0 / d; float3 t0 = (-e - o) * inv; float3 t1 = (e - o) * inv;
    float3 mn = min(t0, t1); float3 mx = max(t0, t1);
    float tn = max(max(mn.x, mn.y), mn.z); float tf = min(min(mx.x, mx.y), mx.z);
    if (tn > tf || tf < 0.001) return;
    float t = tn > 0.001 ? tn : tf;
    if (t >= h.t) return;
    float3 p = (o + d * t) / e; float3 a = abs(p); float3 n;
    if (a.x > a.y && a.x > a.z) n = float3(sign(p.x), 0, 0); else if (a.y > a.z) n = float3(0, sign(p.y), 0); else n = float3(0, 0, sign(p.z));
    h.t = t; h.n = float3(ca * n.x - sa * n.z, n.y, sa * n.x + ca * n.z); h.m = m;
}
void Scene(float3 ro, float3 rd, inout Hit h)
{
    if (rd.y < 0.0) { float t = -ro.y / rd.y; if (t > 0.001 && t < h.t) { h.t = t; h.n = float3(0, 1, 0); h.m = 0; } }
    if (rd.z < 0.0) { float t = (-2.6 - ro.z) / rd.z; if (t > 0.001 && t < h.t) { h.t = t; h.n = float3(0, 0, 1); h.m = 1; } }
    if (rd.y > 0.0)
    {
        float t = (3.6 - ro.y) / rd.y; float3 p = ro + rd * t;
        if (t > 0.001 && t < h.t && abs(p.x) < 2.2 && abs(p.z + 0.2) < 1.4) { h.t = t; h.n = float3(0, -1, 0); h.m = 6; }
    }
    Sphere(ro, rd, float3(-0.95, 0.7, -0.1), 0.7, 2, h);
    Sphere(ro, rd, float3(0.95, 0.5, 0.35), 0.5, 3, h);
    Box(ro, rd, float3(0.35, 0.38, -1.1), float3(0.38, 0.38, 0.38), 0.65, 4, h);
    Sphere(ro, rd, float3(0.05, 0.16, 0.95), 0.16, 5, h);
}
void Material(int m, float3 p, out float3 albedo, out float rough, out float metal, out float3 emit)
{
    emit = 0; metal = 0; rough = 1; albedo = 0.6;
    if (m == 0) { float c = fmod(floor(p.x * 2.0) + floor(p.z * 2.0), 2.0); albedo = lerp(0.16, 0.22, abs(c)); rough = 0.6; }
    else if (m == 1) { albedo = 0.18; rough = 0.9; }
    else if (m == 2) { albedo = float3(1.0, 0.72, 0.02); rough = 0.12; }
    else if (m == 3) { albedo = float3(0.92, 0.93, 0.95); rough = 0.05; metal = 1; }
    else if (m == 4) { albedo = 0.85; rough = 1; }
    else if (m == 5) { emit = float3(6.0, 4.2, 0.6); }
    else { emit = 5.0; }
}
float3 Sky(float3 rd) { return lerp(float3(0.04, 0.04, 0.05), float3(0.32, 0.33, 0.38), saturate(rd.y * 0.5 + 0.5)); }
float3 Trace(float3 ro, float3 rd, inout uint seed)
{
    float3 acc = 0; float3 thr = 1;
    [loop] for (int b = 0; b < 5; b++)
    {
        Hit h; h.t = 1e9; h.n = 0; h.m = -1;
        Scene(ro, rd, h);
        if (h.m < 0) { acc += thr * Sky(rd); break; }
        float3 p = ro + rd * h.t; float3 n = h.n;
        float3 albedo; float rough; float metal; float3 emit;
        Material(h.m, p, albedo, rough, metal, emit);
        if (emit.x + emit.y + emit.z > 0.0) { acc += thr * emit; break; }
        float fres = metal > 0.5 ? 1.0 : 0.04 + 0.96 * pow(1.0 - saturate(dot(-rd, n)), 5.0);
        float3 dir;
        if (Rnd(seed) < fres)
        {
            dir = normalize(reflect(rd, n) + rough * RndSphere(seed));
            if (dot(dir, n) < 0.0) dir = reflect(dir, n);
            if (metal > 0.5) thr *= albedo;
        }
        else
        {
            dir = normalize(n + RndSphere(seed) * 0.999);
            thr *= albedo;
        }
        ro = p + n * 0.001; rd = dir;
        if (b > 1) { float pr = max(max(thr.x, max(thr.y, thr.z)), 0.001); if (Rnd(seed) > pr) break; thr /= pr; }
    }
    return acc;
}
float4 PSStudio(VSOut i) : SV_Target
{
    uint2 px = uint2(i.pos.xy);
    uint seed = Hash(px.x * 1973u + px.y * 9277u + uint(frameIndex) * 26699u) | 1u;
    float2 j = float2(Rnd(seed), Rnd(seed));
    float2 uv = ((floor(i.pos.xy) + j) / res * 2.0 - 1.0) * float2(res.x / res.y, -1.0);
    float3 ro = float3(0.0, 1.25, 4.4); float3 at = float3(0.0, 0.55, 0.0);
    float3 fw = normalize(at - ro); float3 rt = normalize(cross(fw, float3(0, 1, 0))); float3 up = cross(rt, fw);
    float3 rd = normalize(fw * 2.1 + rt * uv.x + up * uv.y);
    return float4(Trace(ro, rd, seed), 1.0);
}
float4 PSResolve(VSOut i) : SV_Target
{
    float4 a = Accum.Load(int3(i.pos.xy, 0));
    float3 c = a.rgb / max(a.a, 1.0);
    c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
    return float4(pow(saturate(c), 1.0 / 2.2), 1);
}
