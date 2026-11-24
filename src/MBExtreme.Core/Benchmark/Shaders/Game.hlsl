// Simulação de jogo: vale com montanhas e um lago, céu de entardecer e dois exércitos (milhares de soldados animados) que avançam,
// trocam disparos de energia e explodem. Pesa na placa como um jogo de verdade: personagens com várias partes, luzes dinâmicas
// das explosões em cada pixel, fumaça e fogo transparentes e um lago com reflexo calculado por raio (ray march no terreno).
#define TERRAIN 220
#define WORLD 80.0
#define WATER -0.9
#define LAKE float2(0.0, 38.0)
#define LAKE_R 12.0
#define LIGHTS 48
#define BEAMS 1200

// ---------------------------------------------------------------- mundo

float Height(float2 xz)
{
    float ring = smoothstep(30.0, 70.0, length(xz));
    float h = Fbm(xz * 0.035 + 3.0) * 26.0 * ring + Fbm(xz * 0.11) * 1.6 - 0.8;
    float lake = smoothstep(LAKE_R, LAKE_R * 0.25, length(xz - LAKE));
    return h - lake * 4.5;
}

float3 GameEye() { float a = 0.9 + time * 0.07 + variation * 2.4; return float3(46.0 * cos(a), 17.0 + 3.0 * sin(time * 0.15), 46.0 * sin(a)); }

static const float3 SunDir = float3(-0.7057, 0.4106, 0.5774); // já normalizado

float3 TerrainNormal(float2 xz)
{
    float e = 0.5;
    float hx = Height(xz + float2(e, 0)) - Height(xz - float2(e, 0));
    float hz = Height(xz + float2(0, e)) - Height(xz - float2(0, e));
    return normalize(float3(-hx, 2.0 * e, -hz));
}

float3 TerrainColor(float2 xz, float h, float3 n)
{
    float slope = 1.0 - n.y;
    float3 grass = lerp(float3(0.16, 0.34, 0.12), float3(0.30, 0.40, 0.16), Noise(xz * 0.3));
    float3 rock = float3(0.40, 0.37, 0.34);
    float3 sand = float3(0.62, 0.54, 0.38);
    float3 col = lerp(grass, rock, smoothstep(0.25, 0.45, slope) + smoothstep(6.0, 12.0, h) * 0.8);
    col = lerp(col, sand, smoothstep(WATER + 1.4, WATER + 0.2, h));
    return lerp(col, float3(0.95, 0.96, 1.0), smoothstep(14.0, 19.0, h + Noise(xz) * 3.0));
}

// Céu de entardecer: degradê, sol com halo e nuvens que andam.
float3 SkyColor(float3 dir)
{
    float y = saturate(dir.y);
    float3 c = lerp(float3(0.95, 0.55, 0.28), float3(0.22, 0.34, 0.62), pow(y, 0.55));
    float s = saturate(dot(dir, SunDir));
    c += float3(1.0, 0.7, 0.35) * (pow(s, 8.0) * 0.45 + pow(s, 600.0) * 6.0);
    if (dir.y > 0.02)
    {
        float2 p = dir.xz / dir.y * 18.0 + float2(time * 0.6, time * 0.2);
        float cloud = smoothstep(0.45, 0.8, Fbm(p * 0.05));
        float3 cloudCol = lerp(float3(0.55, 0.42, 0.45), float3(1.0, 0.78, 0.55), pow(s, 3.0));
        c = lerp(c, cloudCol, cloud * 0.75 * saturate(dir.y * 6.0));
    }
    return c;
}

// Explosões: ponto e instante de cada choque entre unidades (x, y, z, início). Também servem de luz para o chão e os soldados.
StructuredBuffer<float4> Sparks : register(t1);

// Luz das explosões perto do ponto (laranja, apaga em ~1,2 s).
float3 FireLight(float3 world, float3 n)
{
    float3 sum = 0;
    [loop] for (int s = 0; s < LIGHTS; s++)
    {
        float4 sp = Sparks[s];
        float age = time - sp.w;
        if (age < 0.0 || age > 1.2) continue;
        float3 lp = float3(sp.x, world.y + 1.4, sp.z);
        float3 d = lp - world;
        float d2 = dot(d, d);
        float k = (1.0 - age / 1.2); k *= k;
        sum += float3(1.0, 0.55, 0.18) * k * 22.0 * saturate(dot(n, normalize(d)) * 0.7 + 0.3) / (1.0 + d2 * 0.35);
    }
    return sum;
}

float3 Shade(float3 albedo, float3 n, float3 world, float emissive)
{
    float dif = saturate(dot(n, SunDir));
    float3 ambient = lerp(float3(0.35, 0.28, 0.30), float3(0.38, 0.48, 0.70), n.y * 0.5 + 0.5) * 0.55;
    float3 col = albedo * (ambient + dif * float3(1.25, 0.95, 0.72) + FireLight(world, n)) + albedo * emissive * 3.0;
    float dist = length(GameEye() - world);
    float fog = 1.0 - exp(-dist * dist * 0.00004);
    return lerp(col, SkyColor(normalize(world - GameEye())) * 0.9, fog);
}

float3 Tonemap(float3 c) { c *= 1.05; return pow(c / (1.0 + c * 0.6), 0.95); }

// ---------------------------------------------------------------- terreno

struct TerrOut { float4 pos : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 tint : TEXCOORD2; float glow : TEXCOORD3; };

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
    o.tint = TerrainColor(xz, h, o.normal);
    o.glow = 0;
    o.pos = Project(o.world, GameEye(), float3(0, 0, 0), 1.5);
    return o;
}

// ---------------------------------------------------------------- soldados (6 partes: tronco, cabeça, 2 braços e 2 pernas, andando)

// Unidades: a posição vem do processador a cada quadro (x, z, direção, time), como num jogo de verdade.
StructuredBuffer<float4> Units : register(t0);

float3 RotX(float3 p, float a) { float c = cos(a), s = sin(a); return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c); }

TerrOut VSUnits(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float3 C[8] = { float3(-1, -1, -1), float3(1, -1, -1), float3(1, 1, -1), float3(-1, 1, -1), float3(-1, -1, 1), float3(1, -1, 1), float3(1, 1, 1), float3(-1, 1, 1) };
    const int I[36] = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 6, 2, 3, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
    const float3 N[6] = { float3(0, 0, -1), float3(0, 0, 1), float3(0, -1, 0), float3(0, 1, 0), float3(-1, 0, 0), float3(1, 0, 0) };
    // meia-medida, centro e ponto de giro (ombro/quadril) de cada parte
    const float3 Half[6] = { float3(0.17, 0.22, 0.10), float3(0.10, 0.10, 0.10), float3(0.055, 0.20, 0.055), float3(0.055, 0.20, 0.055), float3(0.065, 0.23, 0.065), float3(0.065, 0.23, 0.065) };
    const float3 Center[6] = { float3(0, 0.70, 0), float3(0, 1.04, 0), float3(-0.24, 0.70, 0), float3(0.24, 0.70, 0), float3(-0.09, 0.25, 0), float3(0.09, 0.25, 0) };
    const float Pivot[6] = { 0, 0, 0.90, 0.90, 0.48, 0.48 };
    const float Swing[6] = { 0, 0, 1.0, -1.0, -1.0, 1.0 };

    uint part = vid / 36, corner = vid % 36;
    float4 u = Units[inst];
    bool blue = u.w > 0.5;
    float phase = time * 9.0 + inst * 1.37;
    float swing = sin(phase) * 0.65 * Swing[part];

    float3 local = C[I[corner]] * Half[part] + Center[part];
    float3 n = N[corner / 6];
    if (part >= 2) { local.y -= Pivot[part]; local = RotX(local, swing); local.y += Pivot[part]; n = RotX(n, swing); }
    local.y += abs(sin(phase)) * 0.04;   // balanço da passada

    float heading = u.z;
    float ch = cos(heading), sh = sin(heading);
    local = float3(local.x * ch - local.z * sh, local.y, local.x * sh + local.z * ch);
    n = float3(n.x * ch - n.z * sh, n.y, n.x * sh + n.z * ch);

    TerrOut o;
    o.world = float3(u.x, Height(u.xy), u.y) + local * 0.75;
    o.normal = n;
    float3 armor = blue ? float3(0.16, 0.28, 0.62) : float3(0.70, 0.50, 0.06);
    float3 suit = float3(0.10, 0.10, 0.11);
    float variationTone = 0.85 + 0.3 * Hash21(float2(inst, blue ? 3.0 : 1.0));
    o.tint = (part == 0 || part == 1 ? armor : suit) * variationTone;
    // viseira acesa na frente do capacete
    bool visor = part == 1 && corner / 6 == 1;
    o.glow = visor ? 1.0 : 0.0;
    if (visor) o.tint = blue ? float3(0.3, 0.8, 1.0) : float3(1.0, 0.8, 0.2);
    o.pos = Project(o.world, GameEye(), float3(0, 0, 0), 1.5);
    return o;
}

float4 PSGame(TerrOut i) : SV_Target
{
    float3 n = normalize(i.normal);
    return float4(Tonemap(Shade(i.tint, n, i.world, i.glow)), 1.0);
}

// ---------------------------------------------------------------- lago com reflexo (raio marchando no terreno)

struct WaterOut { float4 pos : SV_Position; float3 world : TEXCOORD0; };

WaterOut VSWater(uint vid : SV_VertexID)
{
    const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
    WaterOut o;
    o.world = float3(LAKE.x + corners[vid].x * LAKE_R * 1.3, WATER, LAKE.y + corners[vid].y * LAKE_R * 1.3);
    o.pos = Project(o.world, GameEye(), float3(0, 0, 0), 1.5);
    return o;
}

float4 PSWater(WaterOut i) : SV_Target
{
    float ground = Height(i.world.xz);
    if (ground > WATER + 0.05) discard;   // margem: o terreno aparece
    float2 w = i.world.xz;
    float e = 0.25;
    float2 flow = float2(time * 0.35, time * 0.22);
    float hx = Noise((w + float2(e, 0)) * 1.7 + flow) - Noise((w - float2(e, 0)) * 1.7 + flow);
    float hz = Noise((w + float2(0, e)) * 1.7 - flow) - Noise((w - float2(0, e)) * 1.7 - flow);
    float3 n = normalize(float3(-hx * 0.35, 1.0, -hz * 0.35));
    float3 eye = GameEye();
    float3 v = normalize(i.world - eye);
    float3 r = reflect(v, n);

    // reflexo: anda pelo raio refletido até bater no terreno (montanhas aparecem de cabeça para baixo no lago)
    float3 refl = SkyColor(r);
    float3 p = i.world + r * 0.3;
    float stepLen = 0.6;
    [loop] for (int k = 0; k < 40; k++)
    {
        p += r * stepLen;
        stepLen *= 1.08;
        float h = Height(p.xz);
        if (p.y < h)
        {
            float3 tn = TerrainNormal(p.xz);
            refl = Shade(TerrainColor(p.xz, h, tn), tn, p, 0);
            break;
        }
        if (p.y > 40.0) break;
    }
    float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(-v, n)), 5.0);
    float depth = saturate((WATER - ground) / 3.5);
    float3 deep = lerp(float3(0.10, 0.22, 0.24), float3(0.02, 0.06, 0.10), depth);
    float3 col = lerp(deep * (0.5 + FireLight(i.world, float3(0, 1, 0)) * 0.3), refl, saturate(fresnel + 0.25));
    col += float3(1.0, 0.75, 0.45) * pow(saturate(dot(r, SunDir)), 220.0) * 4.0;   // brilho do sol na água
    float dist = length(eye - i.world);
    col = lerp(col, SkyColor(v) * 0.9, 1.0 - exp(-dist * dist * 0.00004));
    return float4(Tonemap(col), 1.0);
}

// ---------------------------------------------------------------- disparos de energia entre os exércitos

struct BeamOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; float3 color : TEXCOORD1; float fade : TEXCOORD2; };

BeamOut VSBeams(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float2 corners[6] = { float2(0, -1), float2(1, -1), float2(0, 1), float2(0, 1), float2(1, -1), float2(1, 1) };
    uint per = (uint)units;
    bool blue = (inst & 1u) == 1u;
    uint k = inst >> 1;
    uint shooter = (k * 2654435761u) % per + (blue ? per : 0u);
    uint target = (k * 40503u + 17u) % per + (blue ? 0u : per);
    float4 a = Units[shooter], b = Units[target];
    float3 A = float3(a.x, Height(a.xy) + 0.6, a.y);
    float3 B = float3(b.x, Height(b.xy) + 0.6, b.y);
    float h = Hash21(float2(inst, 9.1));
    float cycle = frac(time * (1.2 + h) + h * 7.0);
    float on = (length(B - A) < 16.0 && cycle < 0.22) ? 1.0 : 0.0;
    float3 eye = GameEye();
    float3 side = normalize(cross(B - A, eye - A)) * 0.06;
    float2 c = corners[vid];
    float3 world = lerp(A, B, c.x) + side * c.y;
    BeamOut o;
    o.pos = on > 0.5 ? Project(world, eye, float3(0, 0, 0), 1.5) : float4(0, 0, -1, 1);
    o.uv = c;
    o.color = blue ? float3(0.25, 0.75, 1.0) : float3(1.0, 0.75, 0.15);
    o.fade = 1.0 - cycle / 0.22;
    return o;
}

float4 PSBeams(BeamOut i) : SV_Target
{
    float core = 1.0 - abs(i.uv.y);
    float glow = core * core * (0.6 + 0.4 * sin(i.uv.x * 40.0 - time * 60.0));
    return float4(i.color * glow * 3.0 * i.fade + core * core * core * i.fade * 2.0, 1.0);
}

// ---------------------------------------------------------------- explosões: onda de choque, bola de fogo e faíscas (somadas) + fumaça

#define FIRE_PARTICLES 26
#define SMOKE_PARTICLES 14

struct FxOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; float3 color : TEXCOORD1; float fade : TEXCOORD2; float kind : TEXCOORD3; float seed : TEXCOORD4; };

FxOut Billboard(float3 center, float size, float2 c, float3 color, float fade, float kind, float seed, bool visible)
{
    float3 rt, up; CamBasis(GameEye(), float3(0, 0, 0), rt, up);
    FxOut o;
    float3 world = center + (rt * c.x + up * c.y) * size;
    o.pos = visible ? Project(world, GameEye(), float3(0, 0, 0), 1.5) : float4(0, 0, -1, 1);
    o.uv = c; o.color = color; o.fade = fade; o.kind = kind; o.seed = seed;
    return o;
}

FxOut VSSparks(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
    uint spark = inst / FIRE_PARTICLES; uint k = inst % FIRE_PARTICLES;
    float4 s = Sparks[spark];
    float age = time - s.w;
    float ground = Height(s.xz);
    float h1 = Hash21(float2(spark, k)), h2 = Hash21(float2(k, spark + 7.0)), h3 = Hash21(float2(spark + k, 3.3));
    float2 c = corners[vid];
    bool alive = spark < (uint)sparks && age >= 0.0;

    if (k == 0)
    {
        // onda de choque no chão
        float life = 0.55, t = saturate(age / life);
        float r = 0.5 + t * 6.0;
        FxOut o;
        float3 world = float3(s.x + c.x * r, ground + 0.08, s.z + c.y * r);
        o.pos = (alive && age < life) ? Project(world, GameEye(), float3(0, 0, 0), 1.5) : float4(0, 0, -1, 1);
        o.uv = c; o.color = float3(1.0, 0.7, 0.35); o.fade = 1.0 - t; o.kind = 0; o.seed = h1;
        return o;
    }
    if (k < 7)
    {
        // bola de fogo: cresce e esfria do amarelo para o vermelho
        float life = 0.9, t = saturate(age / life);
        float3 off = (float3(h1, h2 * 0.6, h3) - 0.5) * 1.2;
        float3 center = float3(s.x, ground + 0.8 + t * 1.4, s.z) + off * (0.4 + t);
        float3 color = lerp(float3(1.0, 0.85, 0.45), float3(0.9, 0.22, 0.04), t);
        return Billboard(center, 0.6 + t * 1.6, c, color, 1.0 - t, 1, h1, alive && age < life);
    }
    // faíscas: saem voando e caem
    float life = 1.1, t = saturate(age / life);
    float3 dir = normalize(float3(h1 - 0.5, 0.6 + h2, h3 - 0.5));
    float3 center = float3(s.x, ground + 0.4, s.z) + dir * (age * (5.0 + 4.0 * h2)) + float3(0, -4.9 * age * age, 0);
    return Billboard(center, 0.12 * (1.0 - t * 0.6), c, lerp(float3(1.0, 0.9, 0.5), float3(1.0, 0.35, 0.05), t), 1.0 - t, 2, h1, alive && age < life);
}

float4 PSSparks(FxOut i) : SV_Target
{
    float d = length(i.uv);
    if (i.kind < 0.5)
    {
        float ring = 1.0 - saturate(abs(d - 0.85) * 8.0);
        return float4(i.color * ring * 1.6 * i.fade, ring * i.fade);
    }
    if (i.kind < 1.5)
    {
        float n = Fbm(i.uv * 2.2 + i.seed * 17.0 + time * 0.8);
        float shape = saturate(1.0 - d * (0.9 + n * 0.6));
        float3 c = i.color * (1.2 + n) * shape * shape * 2.2 * i.fade;
        return float4(c, shape * i.fade);
    }
    float s = 1.0 - saturate(d);
    return float4(i.color * s * s * 2.2 * i.fade, s * i.fade);
}

FxOut VSSmoke(uint vid : SV_VertexID, uint inst : SV_InstanceID)
{
    const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
    uint spark = inst / SMOKE_PARTICLES; uint k = inst % SMOKE_PARTICLES;
    float4 s = Sparks[spark];
    float age = time - s.w - 0.15;
    float life = 3.2, t = saturate(age / life);
    float h1 = Hash21(float2(spark + 11.0, k)), h2 = Hash21(float2(k + 5.0, spark)), h3 = Hash21(float2(spark, k + 23.0));
    float3 drift = float3(h1 - 0.5, 0, h3 - 0.5) * 2.0 + float3(0.8, 0, 0.3) * age;   // o vento leva a fumaça
    float3 center = float3(s.x, Height(s.xz) + 0.8 + age * (1.0 + h2), s.z) + drift * (0.3 + t);
    float shade = 0.18 + 0.12 * h2;
    return Billboard(center, 0.8 + t * 2.6, corners[vid], float3(shade, shade * 0.95, shade * 0.92), 1.0 - t, 3, h1,
        spark < (uint)sparks && age >= 0.0 && age < life);
}

float4 PSSmoke(FxOut i) : SV_Target
{
    float n = Fbm(i.uv * 1.8 + i.seed * 31.0 + time * 0.25);
    float shape = saturate(1.0 - length(i.uv) * (0.85 + n * 0.5));
    float alpha = shape * shape * 0.55 * i.fade;
    float3 lit = i.color * (0.7 + 0.6 * saturate(dot(float3(i.uv, 0.5), SunDir)));
    return float4(lit, alpha);
}

// ---------------------------------------------------------------- céu

float4 PSSky(VSOut i) : SV_Target
{
    float3 eye = GameEye();
    float3 fw = normalize(-eye);
    float3 rt = normalize(cross(fw, float3(0, 1, 0)));
    float3 up = cross(rt, fw);
    float2 ndc = float2(i.uv.x * 2.0 - 1.0, 1.0 - i.uv.y * 2.0);
    float3 dir = normalize(fw + rt * ndc.x * (res.x / res.y) / 1.5 + up * ndc.y / 1.5);
    return float4(Tonemap(SkyColor(dir)), 1.0);
}
