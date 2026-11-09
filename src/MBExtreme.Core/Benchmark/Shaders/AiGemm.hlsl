// Inteligência artificial na placa de vídeo: C = A x B (1024 x 1024) em blocos de 16 x 16 na memória compartilhada.
#define N 1024
#define TILE 16
StructuredBuffer<float> A : register(t0);
StructuredBuffer<float> B : register(t1);
RWStructuredBuffer<float> C : register(u0);
groupshared float As[TILE][TILE];
groupshared float Bs[TILE][TILE];

[numthreads(TILE, TILE, 1)]
void CSGemm(uint3 gid : SV_GroupID, uint3 tid : SV_GroupThreadID)
{
    uint row = gid.y * TILE + tid.y;
    uint col = gid.x * TILE + tid.x;
    float acc = 0.0;
    [loop] for (uint k0 = 0; k0 < N; k0 += TILE)
    {
        As[tid.y][tid.x] = A[row * N + k0 + tid.x];
        Bs[tid.y][tid.x] = B[(k0 + tid.y) * N + col];
        GroupMemoryBarrierWithGroupSync();
        [unroll] for (uint k = 0; k < TILE; k++) acc = mad(As[tid.y][k], Bs[k][tid.x], acc);
        GroupMemoryBarrierWithGroupSync();
    }
    C[row * N + col] = acc;
}
