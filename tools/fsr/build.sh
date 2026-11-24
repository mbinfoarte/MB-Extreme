#!/usr/bin/env bash
# Compila tools/fsr/mbfsr.cpp para Windows x64 (mingw-w64) com os cabeçalhos do AMD FidelityFX SDK e copia para o Core.
# Uso: tools/fsr/build.sh [pasta do FidelityFX-SDK]   (sem pasta, baixa só os cabeçalhos com git)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
sdk="${1:-}"
if [ -z "$sdk" ]; then
  sdk="$(mktemp -d)/fidelityfx-sdk"
  GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 --filter=blob:none --sparse https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK "$sdk"
  git -C "$sdk" sparse-checkout set Kits/FidelityFX/api/include Kits/FidelityFX/upscalers/include Kits/FidelityFX/framegeneration/include
fi
k="$sdk/Kits/FidelityFX"
x86_64-w64-mingw32-g++ -std=c++17 -O2 -shared -static -static-libgcc -static-libstdc++ \
  -I"$k/api/include" -I"$k/upscalers/include" -I"$k/framegeneration/include" \
  "$here/mbfsr.cpp" -o "$root/src/MBExtreme.Core/Benchmark/Native/mbfsr.dll" -Wl,--kill-at
echo "mbfsr.dll pronto"
