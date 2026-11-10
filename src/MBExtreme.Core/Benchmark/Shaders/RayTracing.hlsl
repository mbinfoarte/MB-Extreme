// Ray tracing (DirectX 12, DXR 1.1 em compute shader): arena futurista à noite com esferas cromadas e douradas, anéis girando,
// pilares com neon e piso espelhado. Ligado: sombras, reflexos em 2 quiques e oclusão de ambiente por raio, como nos jogos com RT.
// Desligado: só o primeiro raio com luz simples e reflexo falso do céu (é o "RT desligado" para comparar o custo).
// Compilado para DXIL (cs_6_5) e embutido no programa: veja RayTracing.dxil e o teste ShadersCompilamTests.
#define RS "RootConstants(num32BitConstants=16, b0), RootConstants(num32BitConstants=4, b1), SRV(t0), SRV(t1), SRV(t2), SRV(t3), DescriptorTable(UAV(u0))"

cbuffer Mode : register(b1)
{
    uint rtOn;      // 1 = sombras, reflexos e oclusão por raio
    uint bounces;   // quiques de reflexo
    uint aoRays;    // raios de oclusão por pixel
    uint modePad;
};

RaytracingAccelerationStructure SceneBvh : register(t0);
StructuredBuffer<float4> Verts : register(t1);    // 2 por vértice: posição e normal
StructuredBuffer<uint> Indices : register(t2);    // índices de cada malha (locais à malha)
StructuredBuffer<uint4> Meshes : register(t3);    // por malha: início dos índices, início dos vértices
RWTexture2D<float4> Output : register(u0);

static const float3 KeyDir = float3(-0.5345, 0.5345, 0.6547); // já normalizado

struct Hit { bool hit; float t; float3 p; float3 n; uint mat; uint inst; };

Hit Trace(float3 ro, float3 rd, float tmax)
{
    RayDesc ray;
    ray.Origin = ro; ray.Direction = rd; ray.TMin = 0.001; ray.TMax = tmax;
    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;
    q.TraceRayInline(SceneBvh, RAY_FLAG_NONE, 0xFF, ray);
    while (q.Proceed()) { }
    Hit h;
    h.hit = false; h.t = tmax; h.p = 0; h.n = float3(0, 1, 0); h.mat = 0; h.inst = 0;
    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        uint id = q.CommittedInstanceID();
        uint4 info = Meshes[id & 0xFF];
        uint prim = q.CommittedPrimitiveIndex();
        uint i0 = Indices[info.x + prim * 3] + info.y;
        uint i1 = Indices[info.x + prim * 3 + 1] + info.y;
        uint i2 = Indices[info.x + prim * 3 + 2] + info.y;
        float2 b = q.CommittedTriangleBarycentrics();
        float3 nObj = Verts[i0 * 2 + 1].xyz * (1.0 - b.x - b.y) + Verts[i1 * 2 + 1].xyz * b.x + Verts[i2 * 2 + 1].xyz * b.y;
        float3x4 m = q.CommittedObjectToWorld3x4();
        float3 n = normalize(mul((float3x3)m, nObj));
        h.hit = true;
        h.t = q.CommittedRayT();
        h.p = ro + rd * h.t;
        h.n = dot(n, rd) > 0.0 ? -n : n;
        h.mat = (id >> 8) & 0xFF;
        h.inst = q.CommittedInstanceIndex();
    }
    return h;
}

bool Occluded(float3 ro, float3 rd, float tmax)
{
    RayDesc ray;
    ray.Origin = ro; ray.Direction = rd; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER> q;
    q.TraceRayInline(SceneBvh, RAY_FLAG_NONE, 0xFF, ray);
    while (q.Proceed()) { }
    return q.CommittedStatus() != COMMITTED_NOTHING;
}

float3 NightSky(float3 d)
{
    float y = saturate(d.y);
    float3 c = lerp(float3(0.30, 0.16, 0.10), float3(0.015, 0.02, 0.06), pow(y, 0.45));
    c += float3(0.55, 0.30, 0.05) * pow(saturate(1.0 - abs(d.y) * 4.0), 6.0) * 0.6;     // brilho da cidade no horizonte
    float s = saturate(dot(d, KeyDir));
    c += float3(1.0, 0.85, 0.6) * (pow(s, 900.0) * 18.0 + pow(s, 12.0) * 0.12);           // lua
    float2 cell = floor(d.xz / max(d.y, 0.05) * 60.0);
    float star = step(0.997, Hash21(cell)) * smoothstep(0.1, 0.4, d.y);
    return c + star * 0.8;
}

struct Material { float3 albedo; float3 emissive; float reflectivity; float rough; };

Material GetMaterial(Hit h)
{
    Material m;
    m.emissive = 0; m.rough = 0.0;
    float tone = 0.75 + 0.5 * Hash21(float2(h.inst, 7.0));
    switch (h.mat)
    {
        case 0:   // piso: placas escuras brilhantes com linhas de neon amarelo
        {
            float2 g = h.p.xz / 4.0;
            float2 f = abs(frac(g) - 0.5);
            float neon = smoothstep(0.485, 0.497, max(f.x, f.y));
            float tile = (fmod(floor(g.x) + floor(g.y), 2.0) == 0.0) ? 0.06 : 0.035;
            m.albedo = float3(tile, tile, tile * 1.1);
            m.emissive = float3(1.0, 0.72, 0.0) * neon * 2.2;
            m.reflectivity = 0.45; m.rough = 0.02;
            break;
        }
        case 1: m.albedo = float3(0.92, 0.93, 0.95); m.reflectivity = 0.9; break;                    // cromado
        case 2: m.albedo = float3(1.0, 0.74, 0.28); m.reflectivity = 0.75; break;                    // dourado
        case 3: m.albedo = float3(1.0, 0.77, 0.0) * tone; m.reflectivity = 0.12; m.rough = 0.08; break; // amarelo MB
        case 4: m.albedo = float3(0.12, 0.32, 0.9) * tone; m.reflectivity = 0.12; m.rough = 0.08; break; // azul
        case 5: m.albedo = float3(0.10, 0.10, 0.11); m.reflectivity = 0.3; m.rough = 0.05;            // pilar escuro com faixas de neon
                m.emissive = float3(0.2, 0.75, 1.0) * step(0.93, frac(h.p.y * 0.8)) * 3.0; break;
        default: m.albedo = 0.8; m.reflectivity = 0.2; break;
    }
    return m;
}

float3 Lighting(Hit h, Material m, float3 rd, uint2 pixel, uint bounce)
{
    float dif = saturate(dot(h.n, KeyDir));
    float3 key = float3(1.0, 0.86, 0.7) * 1.6;
    float3 amb = lerp(float3(0.10, 0.07, 0.05), float3(0.05, 0.07, 0.16), h.n.y * 0.5 + 0.5);
    float shadow = 1.0, ao = 0.75;
    if (rtOn != 0)
    {
        shadow = (dif > 0.0 && Occluded(h.p + h.n * 0.002, KeyDir, 400.0)) ? 0.0 : 1.0;
        // oclusão de ambiente: raios curtos em volta da normal
        float open = 0.0;
        uint count = bounce == 0 ? aoRays : 1;
        for (uint k = 0; k < count; k++)
        {
            float2 r = float2(Hash21(float2(pixel) + float2(k * 17.0, frameIndex * 0.37)), Hash21(float2(pixel.yx) + float2(frameIndex * 0.61, k * 5.0)));
            float phi = r.x * 6.2831853;
            float ct = sqrt(1.0 - r.y), st = sqrt(r.y);
            float3 t1 = normalize(abs(h.n.y) < 0.99 ? cross(h.n, float3(0, 1, 0)) : cross(h.n, float3(1, 0, 0)));
            float3 t2 = cross(h.n, t1);
            float3 dir = normalize(t1 * cos(phi) * st + t2 * sin(phi) * st + h.n * ct);
            open += Occluded(h.p + h.n * 0.002, dir, 2.5) ? 0.0 : 1.0;
        }
        ao = count > 0 ? open / count : 1.0;
    }
    return m.albedo * (key * dif * shadow + amb * ao * 2.2) + m.emissive;
}

float3 Shade(float3 ro, float3 rd, uint2 pixel)
{
    float3 color = 0, through = 1;
    uint maxBounce = rtOn != 0 ? bounces : 0;
    for (uint b = 0; b <= maxBounce; b++)
    {
        Hit h = Trace(ro, rd, 600.0);
        if (!h.hit) { color += through * NightSky(rd); break; }
        Material m = GetMaterial(h);
        float fres = m.reflectivity + (1.0 - m.reflectivity) * pow(1.0 - saturate(dot(-rd, h.n)), 5.0) * (m.reflectivity > 0.2 ? 1.0 : 0.25);
        color += through * Lighting(h, m, rd, pixel, b) * (1.0 - fres);
        float3 r = reflect(rd, h.n);
        if (m.rough > 0.0)
        {
            float3 jitter = float3(Hash21(float2(pixel) + b * 3.1 + frameIndex), Hash21(float2(pixel.yx) + b * 7.7), Hash21(float2(pixel) * 1.3 + frameIndex * 0.5)) - 0.5;
            r = normalize(r + jitter * m.rough);
        }
        if (b == maxBounce)
        {
            // sem RT (ou no último quique): reflexo falso só do céu, como os jogos sem ray tracing
            color += through * fres * NightSky(r) * (rtOn != 0 ? 1.0 : 0.6);
            break;
        }
        through *= fres * lerp(float3(1, 1, 1), m.albedo, 0.6);
        ro = h.p + h.n * 0.002;
        rd = r;
    }
    return color;
}

float3 ArenaEye() { float a = 0.6 + time * 0.12 + variation * 2.1; return float3(30.0 * cos(a), 7.5 + 2.0 * sin(time * 0.21), 30.0 * sin(a)); }

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void CSRayTrace(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)res.x || id.y >= (uint)res.y) return;
    float3 eye = ArenaEye();
    float3 rd = CameraRay(float2(id.xy) + 0.5, eye, float3(0, 2.0, 0), 1.35);
    float3 c = Shade(eye, rd, id.xy);
    c = c / (1.0 + c * 0.75);
    c = pow(saturate(c), 1.0 / 1.1);
    VSOut v;
    v.pos = float4(float2(id.xy) + 0.5, 0, 1);
    v.uv = (float2(id.xy) + 0.5) / res;
    float4 o = PSOverlay(v);
    Output[id.xy] = float4(lerp(c, o.rgb, o.a), 1.0);
}
