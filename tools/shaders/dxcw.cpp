// dxcw: compila HLSL com libdxcompiler.so. Uso: dxcw arquivo.hlsl saida.dxil -T cs_6_5 -E Entrada [args...]
#include "dxc/dxcapi.h"
#include <cstdio>
#include <fstream>
#include <sstream>
#include <vector>
#include <string>
static std::wstring W(const char* s) { std::string a(s); return std::wstring(a.begin(), a.end()); }
int main(int argc, char** argv)
{
    if (argc < 3) return 2;
    void* lib = dlopen("libdxcompiler.so", RTLD_NOW);
    if (!lib) { fprintf(stderr, "dlopen: %s\n", dlerror()); return 3; }
    auto create = (DxcCreateInstanceProc)dlsym(lib, "DxcCreateInstance");
    IDxcUtils* utils = nullptr; IDxcCompiler3* comp = nullptr;
    create(CLSID_DxcUtils, IID_PPV_ARGS(&utils));
    create(CLSID_DxcCompiler, IID_PPV_ARGS(&comp));
    std::ifstream in(argv[1], std::ios::binary); std::stringstream ss; ss << in.rdbuf(); std::string src = ss.str();
    DxcBuffer buf{ src.data(), src.size(), DXC_CP_UTF8 };
    std::vector<std::wstring> ws; for (int i = 3; i < argc; i++) ws.push_back(W(argv[i]));
    std::vector<LPCWSTR> args; for (auto& w : ws) args.push_back(w.c_str());
    IDxcResult* res = nullptr;
    if (FAILED(comp->Compile(&buf, args.data(), (UINT32)args.size(), nullptr, IID_PPV_ARGS(&res)))) { fprintf(stderr, "Compile falhou\n"); return 4; }
    IDxcBlobUtf8* err = nullptr; res->GetOutput(DXC_OUT_ERRORS, IID_PPV_ARGS(&err), nullptr);
    if (err && err->GetStringLength()) fprintf(stderr, "%s\n", err->GetStringPointer());
    HRESULT st; res->GetStatus(&st);
    if (FAILED(st)) return 1;
    IDxcBlob* obj = nullptr; res->GetOutput(DXC_OUT_OBJECT, IID_PPV_ARGS(&obj), nullptr);
    std::ofstream out(argv[2], std::ios::binary); out.write((const char*)obj->GetBufferPointer(), obj->GetBufferSize());
    printf("ok %zu bytes\n", (size_t)obj->GetBufferSize());
    return 0;
}
