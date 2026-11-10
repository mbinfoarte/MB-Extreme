// mbfsr.dll: ponte simples entre o MB Extreme (C#) e o AMD FidelityFX SDK (FSR upscaling e geração de quadros, DirectX 12).
// O C# só passa ponteiros e números; as estruturas do SDK são montadas aqui, com os cabeçalhos oficiais, para não errar o
// layout de memória. Compilado com mingw-w64 (tools/fsr/build.sh). Roda num processo separado do teste: se algo falhar no
// código nativo, só esse processo cai.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <cstdio>
#include <cstring>
#include "ffx_api.h"
#include "ffx_api_types.h"
#include "dx12/ffx_api_dx12.h"
#include "ffx_upscale.h"
#include "ffx_framegeneration.h"
#include "dx12/ffx_api_framegeneration_dx12.h"

#define EXPORT extern "C" __declspec(dllexport)

static HMODULE g_lib;
static PfnFfxCreateContext pCreate;
static PfnFfxDestroyContext pDestroy;
static PfnFfxConfigure pConfigure;
static PfnFfxQuery pQuery;
static PfnFfxDispatch pDispatch;
static ffxContext g_up, g_fg, g_sc;
static ffxConfigureDescFrameGeneration g_cfg;
static char g_err[512];
static char g_upVersion[96], g_fgVersion[96];
static uint32_t g_dw, g_dh;

static int Fail(const char* what, int code) { snprintf(g_err, sizeof g_err, "%s (código %d)", what, code); return code == 0 ? -1 : code; }

EXPORT const char* mbfsr_error() { return g_err; }
EXPORT const char* mbfsr_upscaler_version() { return g_upVersion; }
EXPORT const char* mbfsr_framegen_version() { return g_fgVersion; }

// Tamanhos das estruturas (o teste confere com o que o C# espera).
EXPORT int mbfsr_sizeof(int which)
{
    switch (which)
    {
    case 0: return (int)sizeof(FfxApiResource);
    case 1: return (int)sizeof(ffxDispatchDescUpscale);
    case 2: return (int)sizeof(ffxDispatchDescFrameGenerationPrepareV2);
    case 3: return (int)sizeof(ffxConfigureDescFrameGeneration);
    default: return 0;
    }
}

EXPORT int mbfsr_load(const wchar_t* folder)
{
    if (g_lib) return 0;
    SetDllDirectoryW(folder);   // o carregador procura os módulos do upscaler e da geração de quadros na mesma pasta
    wchar_t path[MAX_PATH];
    _snwprintf(path, MAX_PATH, L"%ls\\amd_fidelityfx_loader_dx12.dll", folder);
    g_lib = LoadLibraryW(path);
    if (!g_lib) return Fail("não abriu amd_fidelityfx_loader_dx12.dll", (int)GetLastError());
    pCreate = (PfnFfxCreateContext)GetProcAddress(g_lib, "ffxCreateContext");
    pDestroy = (PfnFfxDestroyContext)GetProcAddress(g_lib, "ffxDestroyContext");
    pConfigure = (PfnFfxConfigure)GetProcAddress(g_lib, "ffxConfigure");
    pQuery = (PfnFfxQuery)GetProcAddress(g_lib, "ffxQuery");
    pDispatch = (PfnFfxDispatch)GetProcAddress(g_lib, "ffxDispatch");
    if (!pCreate || !pDestroy || !pConfigure || !pQuery || !pDispatch) return Fail("funções do FidelityFX não encontradas", -2);
    return 0;
}

// Recurso de textura 2D sem chamar GetDesc (o C# passa tamanho e formato).
static FfxApiResource Res(void* resource, uint32_t w, uint32_t h, uint32_t dxgiFormat, uint32_t state)
{
    FfxApiResource r;
    memset(&r, 0, sizeof r);
    r.resource = resource;
    r.state = state;
    if (!resource) return r;
    r.description.type = FFX_API_RESOURCE_TYPE_TEXTURE2D;
    r.description.format = ffxApiGetSurfaceFormatDX12((DXGI_FORMAT)dxgiFormat);
    r.description.width = w;
    r.description.height = h;
    r.description.depth = 1;
    r.description.mipCount = 1;
    r.description.flags = FFX_API_RESOURCE_FLAGS_NONE;
    r.description.usage = FFX_API_RESOURCE_USAGE_READ_ONLY | FFX_API_RESOURCE_USAGE_UAV;
    return r;
}

static void ProviderVersion(ffxContext* ctx, char* out, size_t cap)
{
    ffxQueryGetProviderVersion q;
    memset(&q, 0, sizeof q);
    q.header.type = FFX_API_QUERY_DESC_TYPE_GET_PROVIDER_VERSION;
    if (pQuery(ctx, &q.header) == FFX_API_RETURN_OK && q.versionName) snprintf(out, cap, "%s", q.versionName);
}

EXPORT int mbfsr_upscale_create(void* device, uint32_t rw, uint32_t rh, uint32_t dw, uint32_t dh)
{
    ffxCreateBackendDX12Desc backend; memset(&backend, 0, sizeof backend);
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
    backend.device = (ID3D12Device*)device;
    ffxCreateContextDescUpscaleVersion version; memset(&version, 0, sizeof version);
    version.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE_VERSION;
    version.version = FFX_UPSCALER_VERSION;
    ffxCreateContextDescUpscale desc; memset(&desc, 0, sizeof desc);
    desc.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    desc.header.pNext = &backend.header;
    backend.header.pNext = &version.header;
    desc.flags = 0;   // cor já comprimida (0 a 1), linear: sem HDR e sem exposição automática
    desc.maxRenderSize = { rw, rh };
    desc.maxUpscaleSize = { dw, dh };
    int rc = (int)pCreate(&g_up, &desc.header, nullptr);
    if (rc != FFX_API_RETURN_OK) return Fail("não criou o FSR (upscaling)", rc);
    ProviderVersion(&g_up, g_upVersion, sizeof g_upVersion);
    g_dw = dw; g_dh = dh;
    return 0;
}

EXPORT int mbfsr_jitter(int index, uint32_t rw, uint32_t dw, float* x, float* y)
{
    int32_t phases = 0;
    ffxQueryDescUpscaleGetJitterPhaseCount pc; memset(&pc, 0, sizeof pc);
    pc.header.type = FFX_API_QUERY_DESC_TYPE_UPSCALE_GETJITTERPHASECOUNT;
    pc.renderWidth = rw; pc.displayWidth = dw; pc.pOutPhaseCount = &phases;
    int rc = (int)pQuery(&g_up, &pc.header);
    if (rc != FFX_API_RETURN_OK || phases <= 0) return Fail("jitter: fases", rc);
    ffxQueryDescUpscaleGetJitterOffset jo; memset(&jo, 0, sizeof jo);
    jo.header.type = FFX_API_QUERY_DESC_TYPE_UPSCALE_GETJITTEROFFSET;
    jo.index = index; jo.phaseCount = phases; jo.pOutX = x; jo.pOutY = y;
    rc = (int)pQuery(&g_up, &jo.header);
    return rc == FFX_API_RETURN_OK ? 0 : Fail("jitter: deslocamento", rc);
}

// Recursos em UNORDERED_ACCESS (como o compute shader deixa). jx/jy: o jitter já com o sinal do FSR (-deslocamento da câmera).
EXPORT int mbfsr_upscale(void* commandList, void* color, void* depth, void* motion, void* output,
    uint32_t rw, uint32_t rh, float jx, float jy, float frameMs, float cameraNear, float cameraFar, float fovY, int reset)
{
    const uint32_t uav = FFX_API_RESOURCE_STATE_UNORDERED_ACCESS;
    ffxDispatchDescUpscale d; memset(&d, 0, sizeof d);
    d.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
    d.commandList = commandList;
    d.color = Res(color, rw, rh, DXGI_FORMAT_R16G16B16A16_FLOAT, uav);
    d.depth = Res(depth, rw, rh, DXGI_FORMAT_R32_FLOAT, uav);
    d.motionVectors = Res(motion, rw, rh, DXGI_FORMAT_R16G16_FLOAT, uav);
    d.exposure = Res(nullptr, 0, 0, 0, uav);
    d.reactive = Res(nullptr, 0, 0, 0, uav);
    d.transparencyAndComposition = Res(nullptr, 0, 0, 0, uav);
    d.output = Res(output, g_dw, g_dh, DXGI_FORMAT_R16G16B16A16_FLOAT, uav);
    d.jitterOffset = { jx, jy };
    d.motionVectorScale = { (float)rw, (float)rh };
    d.renderSize = { rw, rh };
    d.upscaleSize = { g_dw, g_dh };
    d.enableSharpening = true;
    d.sharpness = 0.5f;
    d.frameTimeDelta = frameMs;
    d.preExposure = 1.0f;
    d.reset = reset != 0;
    d.cameraNear = cameraNear;
    d.cameraFar = cameraFar;
    d.cameraFovAngleVertical = fovY;
    d.viewSpaceToMetersFactor = 1.0f;
    d.flags = 0;
    int rc = (int)pDispatch(&g_up, &d.header);
    return rc == FFX_API_RETURN_OK ? 0 : Fail("FSR (upscaling) recusou o quadro", rc);
}

// Troca de corrente com geração de quadros (o FidelityFX cria a swapchain da janela e intercala os quadros gerados).
EXPORT int mbfsr_fg_swapchain(void* factory, void* queue, void* hwnd, uint32_t w, uint32_t h, uint32_t buffers, void** swapchain)
{
    DXGI_SWAP_CHAIN_DESC1 desc; memset(&desc, 0, sizeof desc);
    desc.Width = w; desc.Height = h;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = buffers;
    desc.Scaling = DXGI_SCALING_STRETCH;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    desc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
    IDXGISwapChain4* sc = nullptr;
    ffxCreateContextDescFrameGenerationSwapChainVersionDX12 version; memset(&version, 0, sizeof version);
    version.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATIONSWAPCHAIN_VERSION_DX12;
    version.version = FFX_FRAMEGENERATION_SWAPCHAIN_DX12_VERSION;
    ffxCreateContextDescFrameGenerationSwapChainForHwndDX12 d; memset(&d, 0, sizeof d);
    d.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATIONSWAPCHAIN_FOR_HWND_DX12;
    d.header.pNext = &version.header;
    d.swapchain = &sc;
    d.hwnd = (HWND)hwnd;
    d.desc = &desc;
    d.fullscreenDesc = nullptr;
    d.dxgiFactory = (IDXGIFactory*)factory;
    d.gameQueue = (ID3D12CommandQueue*)queue;
    int rc = (int)pCreate(&g_sc, &d.header, nullptr);
    if (rc != FFX_API_RETURN_OK || !sc) return Fail("não criou a swapchain de geração de quadros", rc);
    *swapchain = sc;
    return 0;
}

static ffxReturnCode_t FgDispatch(ffxDispatchDescFrameGeneration* params, void* user)
{
    return pDispatch((ffxContext*)user, &params->header);
}

EXPORT int mbfsr_fg_create(void* device, void* swapchain, uint32_t dw, uint32_t dh)
{
    ffxCreateBackendDX12Desc backend; memset(&backend, 0, sizeof backend);
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
    backend.device = (ID3D12Device*)device;
    ffxCreateContextDescFrameGenerationVersion version; memset(&version, 0, sizeof version);
    version.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATION_VERSION;
    version.version = FFX_FRAMEGENERATION_VERSION;
    ffxCreateContextDescFrameGeneration desc; memset(&desc, 0, sizeof desc);
    desc.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_FRAMEGENERATION;
    desc.header.pNext = &backend.header;
    backend.header.pNext = &version.header;
    desc.flags = 0;
    desc.displaySize = { dw, dh };
    desc.maxRenderSize = { dw, dh };
    desc.backBufferFormat = FFX_API_SURFACE_FORMAT_R8G8B8A8_UNORM;
    int rc = (int)pCreate(&g_fg, &desc.header, nullptr);
    if (rc != FFX_API_RETURN_OK) return Fail("não criou a geração de quadros", rc);
    ProviderVersion(&g_fg, g_fgVersion, sizeof g_fgVersion);
    g_dw = dw; g_dh = dh;

    memset(&g_cfg, 0, sizeof g_cfg);
    g_cfg.header.type = FFX_API_CONFIGURE_DESC_TYPE_FRAMEGENERATION;
    g_cfg.swapChain = swapchain;
    g_cfg.presentCallback = nullptr;
    g_cfg.frameGenerationCallback = FgDispatch;
    g_cfg.frameGenerationCallbackUserContext = &g_fg;
    g_cfg.frameGenerationEnabled = false;
    g_cfg.generationRect = { 0, 0, (int32_t)dw, (int32_t)dh };
    g_cfg.frameID = 0;
    rc = (int)pConfigure(&g_fg, &g_cfg.header);
    return rc == FFX_API_RETURN_OK ? 0 : Fail("não configurou a geração de quadros", rc);
}

// Um quadro com geração de quadros: configura e prepara (profundidade e movimento) antes de apresentar.
// camera: posição, cima, direita e frente (12 floats).
EXPORT int mbfsr_fg_frame(void* commandList, uint64_t frameId, int enabled, void* depth, void* motion,
    uint32_t rw, uint32_t rh, float jx, float jy, float frameMs, float cameraNear, float cameraFar, float fovY, const float* camera)
{
    g_cfg.frameGenerationEnabled = enabled != 0;
    g_cfg.frameID = frameId;
    g_cfg.flags = 0;
    int rc = (int)pConfigure(&g_fg, &g_cfg.header);
    if (rc != FFX_API_RETURN_OK) return Fail("geração de quadros: configuração do quadro", rc);
    if (!enabled) return 0;
    const uint32_t uav = FFX_API_RESOURCE_STATE_UNORDERED_ACCESS;
    ffxDispatchDescFrameGenerationPrepareV2 p; memset(&p, 0, sizeof p);
    p.header.type = FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATION_PREPARE_V2;
    p.frameID = frameId;
    p.flags = 0;
    p.commandList = commandList;
    p.renderSize = { rw, rh };
    p.jitterOffset = { jx, jy };
    p.motionVectorScale = { (float)rw, (float)rh };
    p.frameTimeDelta = frameMs;
    p.reset = false;
    p.cameraNear = cameraNear;
    p.cameraFar = cameraFar;
    p.cameraFovAngleVertical = fovY;
    p.viewSpaceToMetersFactor = 1.0f;
    p.depth = Res(depth, rw, rh, DXGI_FORMAT_R32_FLOAT, uav);
    p.motionVectors = Res(motion, rw, rh, DXGI_FORMAT_R16G16_FLOAT, uav);
    for (int i = 0; i < 3; i++)
    {
        p.cameraPosition[i] = camera[i];
        p.cameraUp[i] = camera[3 + i];
        p.cameraRight[i] = camera[6 + i];
        p.cameraForward[i] = camera[9 + i];
    }
    rc = (int)pDispatch(&g_fg, &p.header);
    return rc == FFX_API_RETURN_OK ? 0 : Fail("geração de quadros: preparo do quadro", rc);
}

// Encerra na ordem certa: desliga a geração, espera as apresentações pendentes e destrói os contextos.
EXPORT void mbfsr_shutdown()
{
    if (g_fg)
    {
        g_cfg.frameGenerationEnabled = false;
        g_cfg.swapChain = nullptr;
        pConfigure(&g_fg, &g_cfg.header);
    }
    if (g_sc)
    {
        ffxDispatchDescFrameGenerationSwapChainWaitForPresentsDX12 w; memset(&w, 0, sizeof w);
        w.header.type = FFX_API_DISPATCH_DESC_TYPE_FRAMEGENERATIONSWAPCHAIN_WAIT_FOR_PRESENTS_DX12;
        pDispatch(&g_sc, &w.header);
    }
    if (g_fg) { pDestroy(&g_fg, nullptr); g_fg = nullptr; }
    if (g_up) { pDestroy(&g_up, nullptr); g_up = nullptr; }
    if (g_sc) { pDestroy(&g_sc, nullptr); g_sc = nullptr; }
}
