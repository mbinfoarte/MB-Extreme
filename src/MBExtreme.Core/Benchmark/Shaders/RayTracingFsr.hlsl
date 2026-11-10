// Ray tracing com upscaling (AMD FSR): a arena é renderizada numa resolução menor com a câmera tremendo um subpixel por quadro
// (jitter), e o FSR reconstrói a imagem na resolução da tela usando a profundidade e o movimento de cada pixel.
// Montado depois de Common.hlsl e RayTracing.hlsl. CSRayTraceFsr: cor, profundidade e vetores de movimento na resolução
// interna. CSCompose: pega a imagem do FSR, aplica o gama e desenha o FPS e a barra por cima.
#define RS_FSR "RootConstants(num32BitConstants=16, b0), RootConstants(num32BitConstants=4, b1), RootConstants(num32BitConstants=8, b2), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), DescriptorTable(UAV(u0, numDescriptors=3))"
#define RS_COMPOSE "RootConstants(num32BitConstants=16, b0), DescriptorTable(UAV(u0), UAV(u3))"
#define CAM_NEAR 0.1
#define CAM_FAR 600.0

cbuffer Fsr : register(b2)
{
    float2 jitter;      // deslocamento do subpixel neste quadro (pixels da resolução interna)
    float prevTime;     // instante do quadro anterior (para o movimento da câmera)
    float fsrPad0;
    float4 fsrPad1;
};

StructuredBuffer<float4> PrevInstances : register(t4);   // 3 linhas (matriz 3x4) por instância, do quadro anterior
RWTexture2D<float> FsrDepth : register(u1);
RWTexture2D<float2> FsrMotion : register(u2);
RWTexture2D<float4> Upscaled : register(u3);

// Ponto do mundo -> coordenada da tela (0 a 1, y para baixo), na mesma câmera de CameraRay.
float2 ProjectUV(float3 p, float3 eye)
{
    float3 fw = normalize(ArenaAt - eye);
    float3 rt = normalize(cross(fw, float3(0, 1, 0)));
    float3 up = cross(rt, fw);
    float3 d = p - eye;
    float z = max(dot(d, fw), 1e-4);
    float2 s = float2(dot(d, rt), dot(d, up)) / z * ArenaZoom;
    return float2(s.x / (res.x / res.y), -s.y) * 0.5 + 0.5;
}

[RootSignature(RS_FSR)]
[numthreads(8, 8, 1)]
void CSRayTraceFsr(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)res.x || id.y >= (uint)res.y) return;
    float3 eye = ArenaEye(time);
    float3 prevEye = ArenaEye(prevTime);
    float3 rd = CameraRay(float2(id.xy) + 0.5 + jitter, eye, ArenaAt, ArenaZoom);
    Hit first;
    float3 c = Shade(eye, rd, id.xy, first);
    Output[id.xy] = float4(c / (1.0 + c * 0.75), 1.0);      // cor já comprimida (0 a 1), sem gama: o gama vem depois do FSR

    float3 fw = normalize(ArenaAt - eye);
    float depth = 1.0;
    float2 now, before;
    if (first.hit)
    {
        float z = max(dot(first.p - eye, fw), CAM_NEAR);
        depth = CAM_FAR / (CAM_FAR - CAM_NEAR) * (1.0 - CAM_NEAR / z);
        uint i = first.inst * 3;
        float3x4 prev = float3x4(PrevInstances[i], PrevInstances[i + 1], PrevInstances[i + 2]);
        float3 prevWorld = mul(prev, float4(first.objP, 1.0));
        now = ProjectUV(first.p, eye);
        before = ProjectUV(prevWorld, prevEye);
    }
    else
    {
        // céu: só a câmera se mexe
        now = ProjectUV(eye + rd * 1000.0, eye);
        before = ProjectUV(prevEye + rd * 1000.0, prevEye);
    }
    FsrDepth[id.xy] = depth;
    FsrMotion[id.xy] = before - now;                      // da posição atual para a do quadro anterior (unidades de tela)
}

[RootSignature(RS_COMPOSE)]
[numthreads(8, 8, 1)]
void CSCompose(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)res.x || id.y >= (uint)res.y) return;
    float3 c = pow(saturate(Upscaled[id.xy].rgb), 1.0 / 1.1);
    VSOut v;
    v.pos = float4(float2(id.xy) + 0.5, 0, 1);
    v.uv = (float2(id.xy) + 0.5) / res;
    float4 o = PSOverlay(v);
    Output[id.xy] = float4(lerp(c, o.rgb, o.a), 1.0);
}
