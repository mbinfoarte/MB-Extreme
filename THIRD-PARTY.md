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

## FFmpeg (não incluído)
- Uso: gravação de tela acelerada pela placa de vídeo. O MB Extreme **não distribui** o FFmpeg: ele é instalado pelo usuário (winget, pacote Gyan.FFmpeg) e o aplicativo apenas executa o programa.
- Licença: LGPL/GPL conforme a compilação instalada. https://ffmpeg.org
