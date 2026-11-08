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

## FFmpeg (motor de vídeo, incluído como programa separado)
- Uso: captura, codificação, transmissão e replay acelerados pela placa de vídeo. O MB Extreme leva junto, sem modificações, o `ffmpeg.exe` da compilação "release essentials" de https://www.gyan.dev/ffmpeg/builds/ (pasta `ffmpeg` ao lado do programa). Se faltar, o programa baixa o mesmo arquivo do mesmo endereço e confere o SHA-256 publicado. O aplicativo apenas executa o programa (processo separado); nenhum código do FFmpeg é ligado ao MB Extreme.
- Licença: GPL versão 3 (a compilação inclui componentes GPL). Texto da licença: https://www.gnu.org/licenses/gpl-3.0.html. Código-fonte do FFmpeg: https://ffmpeg.org/download.html; as fontes exatas e a configuração da compilação estão publicadas em https://www.gyan.dev/ffmpeg/builds/ e https://github.com/GyanD/codexffmpeg. https://ffmpeg.org

## Vosk (reconhecimento de voz das legendas)
- Uso: legendas automáticas em tempo real, sem internet. O modelo de voz em português (vosk-model-small-pt) é baixado pelo usuário quando ele liga as legendas.
- Licença: Apache 2.0 (biblioteca e modelo). https://alphacephei.com/vosk

## QRCoder
- Uso: gerar o QR do Pix na transmissão.
- Licença: MIT. https://github.com/codebude/QRCoder

## Blender Open Data (dados)
- Uso: índice de velocidade de processadores e placas de vídeo, gerado na compilação a partir dos resultados públicos do Blender Benchmark, para mostrar a posição do PC do usuário. O MB Extreme só distribui o índice resumido (nome do modelo e nota), não os resultados originais.
- Licença dos dados: Creative Commons Zero (CC0, domínio público). https://opendata.blender.org

## dualshock-tools (protocolo de calibração dos controles PlayStation)
- Uso: a tela "Calibrar controle" segue o protocolo publicado por este projeto para ler as informações, calibrar o centro e o alcance dos analógicos e gravar na memória de DualShock 4 e DualSense (código próprio em C#, em `src/MBExtreme.Core/Gamepads`). https://github.com/dualshock-tools/dualshock-tools.github.io
- Licença: MIT.

```
MIT License

Copyright (c) 2024 the_al

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
