# Componentes de terceiros

| Componente | Uso | Licença |
|---|---|---|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Leitura de sensores (temperatura, carga, ventoinhas, energia) | MPL 2.0 |
| [xUnit](https://xunit.net) | Testes | Apache 2.0 |

A leitura de temperatura da CPU em versões recentes usa o driver **PawnIO** (instalado à parte, por exemplo com `winget install namazso.PawnIO`).
Ao distribuir o programa, mantenha os avisos de licença acima.

Ideias e técnicas de outros projetos usados apenas como referência de estudo estão em `docs/REFERENCIA-*.md`; nenhum código foi copiado.

## Microsoft.Diagnostics.Tracing.TraceEvent
- Uso: leitura dos eventos ETW de apresentação de quadros (monitor de FPS).
- Licença: MIT. https://github.com/microsoft/perfview

## System.Drawing.Common
- Uso: captura de tela e codificação JPEG da gravação de tela.
- Licença: MIT.

## Vortice.Windows (Vortice.Direct3D11, Vortice.D3DCompiler)
- Uso: benchmark 3D da GPU em Direct3D 11.
- Licença: MIT. https://github.com/amerkoleci/Vortice.Windows

## Microsoft.Data.Sqlite (servidor de licenças)
- Uso: banco de dados do servidor de licenças de modelo (`src/MBExtreme.LicenseServer`). Não faz parte do aplicativo instalado.
- Licença: MIT.

## NAudio (NAudio.Core, NAudio.Wasapi)
- Uso: captura do som do PC e do microfone (WASAPI) e mixagem para a gravação e a transmissão.
- Licença: MIT. https://github.com/naudio/NAudio

## System.Security.Cryptography.ProtectedData
- Uso: guardar a chave de transmissão protegida pelo Windows (DPAPI), se o usuário pedir para lembrar.
- Licença: MIT.

## AnyDesk (não incluído)
- Uso: suporte remoto. O MB Extreme **não distribui** o AnyDesk: ele é baixado do site oficial (download.anydesk.com) só quando o cliente pede, tem a assinatura digital conferida e é aberto pelo próprio cliente. Programa proprietário, sujeito aos termos do fabricante (uso pessoal gratuito; atendimento comercial exige licença do AnyDesk). https://anydesk.com

## FFmpeg (não incluído)
- Uso: captura, codificação, transmissão e replay acelerados pela placa de vídeo. O MB Extreme **não distribui** o FFmpeg: ele é instalado pelo usuário (winget, pacote Gyan.FFmpeg) e o aplicativo apenas executa o programa.
- Licença: LGPL/GPL conforme a compilação instalada. https://ffmpeg.org

## Vosk (reconhecimento de voz das legendas)
- Uso: legendas automáticas em tempo real, sem internet. O modelo de voz em português (vosk-model-small-pt) é baixado pelo usuário quando ele liga as legendas.
- Licença: Apache 2.0 (biblioteca e modelo). https://alphacephei.com/vosk

## QRCoder
- Uso: gerar o QR do Pix na transmissão.
- Licença: MIT. https://github.com/codebude/QRCoder

## Blender Open Data (dados)
- Uso: índice de velocidade de processadores e placas de vídeo, gerado na compilação a partir dos resultados públicos do Blender Benchmark, para mostrar a posição do PC do usuário. O MB Extreme só distribui o índice resumido (nome do modelo e nota), não os resultados originais.
- Licença dos dados: Creative Commons Zero (CC0, domínio público). https://opendata.blender.org
