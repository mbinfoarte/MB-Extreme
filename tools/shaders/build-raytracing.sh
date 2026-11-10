#!/usr/bin/env bash
# Recompila o sombreador de ray tracing (DX12, cs_6_5) para DXIL e atualiza o hash da fonte.
# Precisa de g++, curl e do pacote NuGet Vortice.Dxc.Native (libdxcompiler.so + libdxil.so, que assina o DXIL).
# Rode depois de mudar Common.hlsl ou RayTracing.hlsl:  tools/shaders/build-raytracing.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
shaders="$root/src/MBExtreme.Core/Benchmark/Shaders"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
native="${DXC_NATIVE:-$(ls -d ~/.nuget/packages/vortice.dxc.native/*/runtimes/linux-x64/native | tail -1)}"
mkdir -p "$work/inc/dxc"
for f in dxcapi.h WinAdapter.h; do curl -sSfo "$work/inc/dxc/$f" "https://raw.githubusercontent.com/microsoft/DirectXShaderCompiler/main/include/dxc/$f"; done
g++ -std=c++17 -O1 -I"$work/inc" -I"$work/inc/dxc" "$here/dxcw.cpp" -o "$work/dxcw" -ldl
# mesma montagem do programa (GpuWindowBench.ShaderSource): Common + "\n" + RayTracing
python3 -c "import sys; open(sys.argv[3],'w',newline='').write(open(sys.argv[1],encoding='utf-8').read()+'\n'+open(sys.argv[2],encoding='utf-8').read())" \
  "$shaders/Common.hlsl" "$shaders/RayTracing.hlsl" "$work/rt.hlsl"
LD_LIBRARY_PATH="$native" "$work/dxcw" "$work/rt.hlsl" "$shaders/RayTracing.dxil" -T cs_6_5 -E CSRayTrace -HV 2021 -O3 -Qstrip_debug -Qstrip_reflect
sha=$(python3 -c "import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],"rb").read().replace(b"\r\n",b"\n")).hexdigest())" "$work/rt.hlsl")
sed -i -E "s/(RayTracingSourceSha256 = \")[0-9a-f]*(\")/\1$sha\2/" "$root/src/MBExtreme.Core/Benchmark/RtScene.cs"
echo "RayTracing.dxil atualizado (fonte $sha)"
