# Roadmap

## No MB Extreme (Beta 01)
- [x] Tema preto e amarelo, menu lateral por módulos, janela de progresso padrão
- [x] Painel com medidores (CPU, GPU, memória, ping), gráficos e limpeza segura
- [x] Memória: status, limpeza e lista de protegidos
- [x] Limpeza de temporários, Prefetch, cache e relatórios
- [x] DNS: teste, ranking, aplicar e restaurar
- [x] Motor de ajustes reversíveis (energia, jogos, rede, privacidade, boot, periféricos, efeitos visuais, energia para jogos) e **Perfis** (Jogo, Competitivo, PC fraco)
- [x] Remoção de aplicativos do Windows com ponto de restauração
- [x] Inicialização: programas que abrem com o Windows (liga/desliga sem apagar)
- [x] Informações do sistema e **Monitor em tempo real** (clock e uso por núcleo, tensões, temperaturas, ventoinhas, memória, discos)
- [x] **Meu hardware**: alertas (XMP/EXPO, canal único, Windows 24H2, BIOS antiga, microcódigo Intel, chipset AMD, Resizable BAR)
- [x] **BIOS e atualizações** e **Receita de BIOS** (memória, Resizable BAR, PBO/Curve Optimizer)
- [x] **Benchmark** (CPU, vetorial, criptografia, compressão, memória com latência, disco, GPU 3D) e **Antes e depois**
- [x] **Teste de estresse** geral e **por núcleo** (Curve Optimizer)
- [x] **Diagnóstico de latência** (DPC/ISR por driver)
- [x] **Modo Jogo** automático e **Assistente de jogo**
- [x] **Monitor de FPS** com overlay
- [x] **Gravação e transmissão** pela placa de vídeo (NVENC, AMF, Quick Sync; 1080p/1440p/4K; 30/60 fps): tela ou janela, áudio do PC e microfone, transmissão RTMP (Twitch, YouTube, outro servidor) e replay dos melhores momentos; modo compatível AVI
- [x] **Reparo do Windows** (SFC, DISM, CHKDSK, Windows Update, loja, WMI, rede, cache de ícones) com saída ao vivo
- [x] Janela sem moldura nativa; **Painel** com gráfico animado de internet (ping, latência, ping local, DNS, rota) e itens de atenção
- [x] Páginas de **Processador**, **Placa de vídeo**, **Memória** (limpeza automática), **Discos** e **Pendrives e HDs externos** (remoção segura)
- [x] **Segurança** (antivírus, firewall, UAC), **Programas recomendados** e **Ferramentas** (processos, arquivos grandes e duplicados, pastas vazias e atalhos inválidos, destruir arquivos, desligamento automático)
- [x] **Licenciamento**: módulo no app (login por código, licença assinada, duas últimas guardadas, renovação, plano Pro), servidor e site de modelo, testes de ponta a ponta (`docs/LICENCIAMENTO.md`)
- [x] Janela arredondada, menu compacto com cartão do cliente (foto, nome, plano e dias restantes), correções automáticas de segurança e limpeza automática da memória no painel, **Suporte remoto** com AnyDesk
- [x] Dispositivos, cache de portas USB, armazenamento e saúde dos discos
- [x] Analisador de rede (Ping, TCPing, MTR, jitter) e diagnóstico de Wi-Fi
- [x] Drivers (NVIDIA direto do servidor oficial, AMD e Intel pelo Windows Update) e programas (winget)
- [x] Instalador (.exe) com barra de progresso, mini tutorial, atalhos e desinstalação

## Próximos passos
- [ ] Suporte: pedido enviado ao servidor com o ID do AnyDesk, painel do técnico com clientes disponíveis e histórico de atendimentos
- [ ] Colocar o licenciamento em produção: hospedagem, gateway de pagamento, e-mails, painel administrativo e termos (checklist em `docs/LICENCIAMENTO.md`)
- [ ] Gerenciador de menu de contexto e de programas padrão
- [ ] Assinatura de código do instalador (evita o aviso do SmartScreen)
- [ ] Estúdio: cenas e sobreposições (webcam, texto), gravação e transmissão em qualidades diferentes, atalhos configuráveis
- [ ] Central de ações (histórico geral e desfazer tudo)
- [ ] Base de versões recomendadas de BIOS ampliada (outras plataformas e fabricantes)
- [ ] Ajustes planejados do `CATALOGO-AJUSTES.md` (TCP, bcdedit com medição, armazenamento)
- [ ] Ping por região de jogo e catálogo de servidores; relays e seleção automática de rota
- [ ] Coleta de métricas (opt-in) e motor de recomendação
- [ ] Atualização automática, licenciamento, site e suporte
