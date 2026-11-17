#!/usr/bin/env bash
# Recompila os sombreadores de ray tracing (DX12, cs_6_5) para DXIL e atualiza os hashes das fontes em RtScene.cs.
# Precisa de g++, curl, python3 e do pacote NuGet Vortice.Dxc.Native (libdxcompiler.so + libdxil.so, que assina o DXIL).
# Rode depois de mudar Common.hlsl, RayTracing.hlsl ou RayTracingFsr.hlsl:  tools/shaders/build-raytracing.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
shaders="$root/src/MBExtreme.Core/Benchmark/Shaders"
scene="$root/src/MBExtreme.Core/Benchmark/RtScene.cs"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
native="${DXC_NATIVE:-$(ls -d ~/.nuget/packages/vortice.dxc.native/*/runtimes/linux-x64/native | tail -1)}"
mkdir -p "$work/inc/dxc"
for f in dxcapi.h WinAdapter.h; do curl -sSfo "$work/inc/dxc/$f" "https://raw.githubusercontent.com/microsoft/DirectXShaderCompiler/main/include/dxc/$f"; done
g++ -std=c++17 -O1 -I"$work/inc" -I"$work/inc/dxc" "$here/dxcw.cpp" -o "$work/dxcw" -ldl

# junta as fontes como o programa faz (GpuWindowBench.ShaderSource): partes separadas por "\n", finais de linha LF
join() { python3 - "$@" <<'PY'
import sys
out = sys.argv[1]
parts = [open(p, encoding='utf-8').read().replace('\r\n', '\n') for p in sys.argv[2:]]
open(out, 'w', newline='').write('\n'.join(parts))
PY
}
sha() { python3 -c "import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest())" "$1"; }
compile() { LD_LIBRARY_PATH="$native" "$work/dxcw" "$1" "$2" -T cs_6_5 -E "$3" -HV 2021 -O3 -Qstrip_debug -Qstrip_reflect; }

join "$work/rt.hlsl" "$shaders/Common.hlsl" "$shaders/RayTracing.hlsl"
join "$work/fsr.hlsl" "$shaders/Common.hlsl" "$shaders/RayTracing.hlsl" "$shaders/RayTracingFsr.hlsl"
compile "$work/rt.hlsl" "$shaders/RayTracing.dxil" CSRayTrace
compile "$work/fsr.hlsl" "$shaders/RayTracingFsr.dxil" CSRayTraceFsr
compile "$work/fsr.hlsl" "$shaders/RayTracingCompose.dxil" CSCompose
a=$(sha "$work/rt.hlsl"); b=$(sha "$work/fsr.hlsl")
sed -i -E "s/(RayTracingSourceSha256 = \")[0-9a-f]*(\")/\1$a\2/; s/(FsrSourceSha256 = \")[0-9a-f]*(\")/\1$b\2/" "$scene"
echo "DXIL atualizado (ray tracing $a, FSR $b)"
