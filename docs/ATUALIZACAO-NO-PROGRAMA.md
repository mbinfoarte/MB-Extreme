# Atualização dentro do programa

## Como funciona
1. O CI numera cada compilação (0.9.N, N = número da execução) e, ao final, **avisa o servidor** (`POST /v1/admin/app/latest`) com um manifesto: `build`, `version`, `url` (https), `sha256` do instalador, `size`, `notes` e `minimumBuild`.
2. O programa pergunta ao servidor (`GET /v1/app/latest`) ao abrir e a cada 6 horas.
3. Compilação maior que a instalada: **aviso no alto** com "Atualizar agora" e "Depois".
4. Compilação instalada **menor que `minimumBuild`**: tela de **atualização obrigatória** (só dá para atualizar).
5. "Atualizar agora" baixa o instalador, **confere o SHA-256** (apaga se não bater), fecha o programa e abre o instalador.

## O que precisa ser configurado (uma vez)
- No servidor: `Updates:PublishToken` (texto secreto de 16+ caracteres).
- No GitHub (repositório): variável `MBX_SERVER_URL` (endereço https do servidor), segredo `MBX_UPDATE_TOKEN` (o mesmo token) e, se o repositório for privado, variável `MBX_DOWNLOAD_BASE` com o endereço **público** onde os instaladores ficam (o download direto de um repositório privado exige login e não funciona para o cliente).
- No programa: `AppInfo.DefaultServerUrl` preenchido (ou o campo de servidor nas configurações).

## Para tornar uma versão obrigatória
Envie o manifesto com `minimumBuild` igual ao número da compilação que passa a ser exigida (todas as menores serão bloqueadas até atualizar).

## Segurança
- Só https; sem usuário e senha na URL; o arquivo só é executado se o SHA-256 conferir.
- O token de publicação nunca vai ao programa; sem token no servidor, o envio fica desligado.
