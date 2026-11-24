// MB Bench: definições comuns a todas as cenas da janela 3D.
cbuffer Frame : register(b0)
{
    float time;       // segundos desde o começo da cena
    float progress;   // 0 a 1 (barra de baixo)
    float fps;        // FPS mostrado no canto (negativo = "---")
    float frameIndex; // quadro atual (sementes do path tracing)
    float2 res;       // resolução em pixels
    float spp;        // amostras por desenho (estúdio)
    float units;      // unidades por exército (simulação de jogo)
    float4 tile;      // quadrado do render em blocos: x, y, tamanho, 1 = ligado
    float variation;  // muda a câmera a cada passada (a imagem não fica sempre igual)
    float sparks;     // quantas explosões na simulação de jogo
    float2 pad;
};

struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

VSOut VSFull(uint id : SV_VertexID)
{
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    o.uv = uv;
    return o;
}

float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

float Noise(float2 p)
{
    float2 i = floor(p); float2 f = frac(p); f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float Fbm(float2 p)
{
    float v = 0.0; float a = 0.5;
    [unroll] for (int k = 0; k < 5; k++) { v += a * Noise(p); p = p * 2.03 + float2(1.7, 9.2); a *= 0.5; }
    return v;
}

// Projeção de um ponto do mundo pela câmera (olho em eye, olhando para at). Profundidade linear de 0,1 a 600.
float4 Project(float3 p, float3 eye, float3 at, float zoom)
{
    float3 fw = normalize(at - eye);
    float3 rt = normalize(cross(fw, float3(0, 1, 0)));
    float3 up = cross(rt, fw);
    float3 d = p - eye;
    float x = dot(d, rt), y = dot(d, up), z = dot(d, fw);
    return float4(x * zoom * res.y / res.x, y * zoom, (z - 0.1) / 599.9 * z, z);
}

void CamBasis(float3 eye, float3 at, out float3 rt, out float3 up)
{
    float3 fw = normalize(at - eye);
    rt = normalize(cross(fw, float3(0, 1, 0)));
    up = cross(rt, fw);
}

float3 CameraRay(float2 pixel, float3 eye, float3 at, float zoom)
{
    float2 uv = (pixel / res * 2.0 - 1.0) * float2(res.x / res.y, -1.0);
    float3 fw = normalize(at - eye);
    float3 rt = normalize(cross(fw, float3(0, 1, 0)));
    float3 up = cross(rt, fw);
    return normalize(fw * zoom + rt * uv.x + up * uv.y);
}

// ---- contador de FPS e barra de progresso desenhados por cima de tudo (fonte de 7 segmentos)
float SegDist(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// p: 0..1 na largura, 0..2 na altura (y para cima). Bits: a=1 b=2 c=4 d=8 e=16 f=32 g=64.
float SevenSeg(float2 p, int mask, float w)
{
    float d = 10.0;
    if (mask & 1)  d = min(d, SegDist(p, float2(0.18, 1.88), float2(0.82, 1.88)));
    if (mask & 2)  d = min(d, SegDist(p, float2(0.9, 1.08), float2(0.9, 1.8)));
    if (mask & 4)  d = min(d, SegDist(p, float2(0.9, 0.2), float2(0.9, 0.92)));
    if (mask & 8)  d = min(d, SegDist(p, float2(0.18, 0.12), float2(0.82, 0.12)));
    if (mask & 16) d = min(d, SegDist(p, float2(0.1, 0.2), float2(0.1, 0.92)));
    if (mask & 32) d = min(d, SegDist(p, float2(0.1, 1.08), float2(0.1, 1.8)));
    if (mask & 64) d = min(d, SegDist(p, float2(0.18, 1.0), float2(0.82, 1.0)));
    return 1.0 - smoothstep(w * 0.7, w, d);
}

int DigitMask(int d)
{
    int masks[10] = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };
    return masks[clamp(d, 0, 9)];
}

float4 PSOverlay(VSOut i) : SV_Target
{
    float s = res.y / 1080.0;
    float2 px = i.pos.xy / s;
    float4 o = float4(0, 0, 0, 0);
    // caixa do FPS
    if (px.x > 24 && px.x < 300 && px.y > 24 && px.y < 108)
    {
        o = float4(0.04, 0.04, 0.05, 0.78);
        if (px.x < 30) o = float4(1.0, 0.77, 0.0, 1.0);
        int n = (int)round(fps);
        float ink = 0.0;
        [unroll] for (int k = 0; k < 4; k++)
        {
            float2 cell = (px - float2(48 + k * 38, 96)) / float2(30, -32); // altura do dígito: 64 px
            if (cell.x >= -0.1 && cell.x <= 1.1 && cell.y >= -0.1 && cell.y <= 2.1)
            {
                int place = (k == 0) ? 1000 : (k == 1) ? 100 : (k == 2) ? 10 : 1;
                int mask = 0;
                if (fps < 0.0) mask = (k < 3) ? 64 : 0;
                else if (n >= place || k == 3) mask = DigitMask((n / place) % 10);
                ink = max(ink, SevenSeg(cell, mask, 0.13));
            }
        }
        // "FPS" pequeno em amarelo
        float label = 0.0;
        int letters[3] = { 113, 115, 109 };
        [unroll] for (int l = 0; l < 3; l++)
        {
            float2 cell = (px - float2(206 + l * 26, 92)) / float2(18, -18);
            if (cell.x >= -0.1 && cell.x <= 1.1 && cell.y >= -0.1 && cell.y <= 2.1) label = max(label, SevenSeg(cell, letters[l], 0.16));
        }
        o.rgb = lerp(o.rgb, float3(0.96, 0.96, 0.97), ink);
        o.rgb = lerp(o.rgb, float3(1.0, 0.77, 0.0), label);
    }
    // barra de progresso embaixo
    float barH = 7.0 * s;
    if (i.pos.y > res.y - barH)
    {
        float t = i.pos.x / res.x;
        o = t <= progress ? float4(lerp(float3(1.0, 0.85, 0.3), float3(1.0, 0.54, 0.0), t), 1.0) : float4(0.08, 0.08, 0.09, 0.85);
    }
    return o;
}
