// Simulação de jogo: terreno com montanhas, céu e dois exércitos (milhares de unidades) que avançam e se misturam no vale.
#define TERRAIN 220
#define WORLD 80.0

struct TerrOut { float4 pos : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 tint : TEXCOORD2; };

float Height(float2 xz)
{
    float ring = smoothstep(30.0, 70.0, length(xz));
    return Fbm(xz * 0.035 + 3.0) * 26.0 * ring + Fbm(xz * 0.11) * 1.6 - 0.8;
}

float3 GameEye() { float a = 0.9 + time * 0.07 + variation * 2.4; return float3(46.0 * cos(a), 17.0 + 3.0 * sin(time * 0.15), 46.0 * sin(a)); }

float3 TerrainNormal(float2 xz)
{
    float e = 0.5;
    float hx = Height(xz + float2(e, 0)) - Height(xz - float2(e, 0));
    float hz = Height(xz + float2(0, e)) - Height(xz - float2(0, e));
    return normalize(float3(-hx, 2.0 * e, -hz));
}

TerrOut VSTerrain(uint vid : SV_VertexID)
{
    const float2 corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
    uint quad = vid / 6; float2 c = corners[vid % 6];
    float2 g = float2(quad % (TERRAIN - 1), quad / (TERRAIN - 1)) + c;
    float2 xz = g / (TERRAIN - 1) * (2.0 * WORLD) - WORLD;
    float h = Height(xz);
    TerrOut o;
    o.world = float3(xz.x, h, xz.y);
    o.normal = TerrainNormal(xz);
    float slope = 1.0 - o.normal.y;
    float3 grass = lerp(float3(0.16, 0.36, 0.14), float3(0.28, 0.42, 0.18), Noise(xz * 0.3));
    float3 rock = float3(0.42, 0.4, 0.38);
    float3 col = lerp(grass, rock, smoothstep(0.25, 0.45, slope) + smoothstep(6.0, 12.0, h) * 0.8);
    o.tint = lerp(col, float3(0.95, 0.96, 1.0), smoothstep(14.0, 19.0, h + Noise(xz) * 3.0));
    o.pos = Project(o.world, GameEye(), float3(0, 0, 0), 1.5);
    return o;
}

// Unidades: a posição vem do processador a cada quadro (x, z, direção, time), como num jogo de verdade.
StructuredBuffer<float4> Units : register(t0);
// Explosões: ponto e instante de cada choque entre unidades (x, y, z, início).
StructuredBuffer<float4> Sparks : register(t1);

TerrOut VSUnits(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float3 C[8] = { float3(-1, -1, -1), float3(1, -1, -1), float3(1, 1, -1), float3(-1, 1, -1), float3(-1, -1, 1), float3(1, -1, 1), float3(1, 1, 1), float3(-1, 1, 1) };
    const int I[36] = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 6, 2, 3, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
    const float3 N[6] = { float3(0, 0, -1), float3(0, 0, 1), float3(0, -1, 0), float3(0, 1, 0), float3(-1, 0, 0), float3(1, 0, 0) };
    float4 u = Units[inst];
    bool blue = u.w > 0.5;
    float heading = u.z;
    float3 local = C[I[vid]] * float3(0.2, 0.3, 0.16);
    float ch = cos(heading), sh = sin(heading);
    local = float3(local.x * ch - local.z * sh, local.y, local.x * sh + local.z * ch);
    float3 n = N[vid / 6];
    n = float3(n.x * ch - n.z * sh, n.y, n.x * sh + n.z * ch);
    TerrOut o;
    o.world = float3(u.x, Height(u.xy) + 0.3, u.y) + local;
    o.normal = n;
    o.tint = (blue ? float3(0.22, 0.42, 1.0) : float3(1.0, 0.72, 0.08)) * (0.8 + 0.4 * Hash21(float2(inst, blue ? 3.0 : 1.0)));
    o.pos = Project(o.world, GameEye(), float3(0, 0, 0), 1.5);
    return o;
}

#define SPARK_PARTICLES 24

struct SparkOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; float3 color : TEXCOORD1; float fade : TEXCOORD2; };

SparkOut VSSparks(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
    uint spark = inst / SPARK_PARTICLES; uint k = inst % SPARK_PARTICLES;
    float4 s = Sparks[spark];
    float age = time - s.w;
    float life = 1.1;
    float t = saturate(age / life);
    float h1 = Hash21(float2(spark, k)), h2 = Hash21(float2(k, spark + 7.0)), h3 = Hash21(float2(spark + k, 3.3));
    float3 dir = normalize(float3(h1 - 0.5, 0.6 + h2, h3 - 0.5));
    float3 center = float3(s.x, Height(s.xz) + 0.4, s.z) + dir * (age * (4.0 + 3.0 * h2)) + float3(0, -4.9 * age * age, 0);
    float3 rt, up; CamBasis(GameEye(), float3(0, 0, 0), rt, up);
    float size = 0.18 * (1.0 - t * 0.6);
    float2 c = corners[vid];
    SparkOut o;
    float3 world = center + (rt * c.x + up * c.y) * size;
    o.pos = (spark < (uint)sparks && age >= 0.0 && age < life) ? Project(world, GameEye(), float3(0, 0, 0), 1.5) : float4(0, 0, -1, 1);
    o.uv = c;
    o.color = lerp(float3(1.0, 0.9, 0.5), float3(1.0, 0.35, 0.05), t);
    o.fade = 1.0 - t;
    return o;
}

float4 PSSparks(SparkOut i) : SV_Target
{
    float d = 1.0 - saturate(length(i.uv));
    return float4(i.color * d * d * 2.0 * i.fade, d * i.fade);
}

float4 PSGame(TerrOut i) : SV_Target
{
    float3 n = normalize(i.normal);
    float3 sun = normalize(float3(-0.5, 0.75, 0.35));
    float dif = saturate(dot(n, sun));
    float3 col = i.tint * (0.32 * float3(0.75, 0.82, 1.0) * (0.6 + 0.4 * n.y) + 1.0 * dif * float3(1.0, 0.95, 0.85));
    float dist = length(GameEye() - i.world);
    float fog = 1.0 - exp(-dist * dist * 0.00004);
    col = lerp(col, float3(0.62, 0.7, 0.85), fog);
    return float4(pow(col, 0.95), 1.0);
}

float4 PSSky(VSOut i) : SV_Target
{
    float t = i.pos.y / res.y;
    float3 c = lerp(float3(0.3, 0.45, 0.78), float3(0.68, 0.75, 0.88), saturate(t * 1.6));
    return float4(c, 1.0);
}
