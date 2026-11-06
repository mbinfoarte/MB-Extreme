# Status do projeto (duas frentes separadas)

Atualizado em 27/10/2026. Use este arquivo para retomar o trabalho sem depender do histórico da conversa.

## Frente A: Aplicativo MB Extreme (Windows, .NET 8, WPF)
**Publicado até o beta-45** (câmera pelo relé, como o OBS). Em commit local, ainda sem subir: plano do MB Optimizer dentro do MB Extreme — auditoria (`docs/AUDITORIA-MB-OPTIMIZER.md`) e Fase 1: núcleo de operações (fila, estados, diário), Central Desfazer, uma janela só, Voltar com Alt+← e botão do mouse, selo "Licença: Grátis". Fase 2: Painel inteligente (análise rápida e profunda só leitura, correção seletiva pela fila, antes e depois, histórico e relatório).

Pendências, em ordem sugerida:
1. Testar o beta-38 no PC: Modo leve da live, Otimizar a live, vídeo ao vivo das câmeras (inclusive celular por endereço de rede), áudio profissional, bolinha REC, grupo NVIDIA da biblioteca, capas com SteamGridDB.
2. Tela de Início menor (precisa de prints do usuário).
3. Testar no PC o desempenho do estúdio: gravar e transmitir com câmera (câmera não pode travar), codificadores da placa aparecendo sozinhos, motor de vídeo sem instalar nada e o monitor de uso (contadores "GPU Engine").
4. Testar no PC o estúdio novo: troca de cena ao vivo (ddagrab de verdade, câmeras), imagem "No ar", janela por cena e o envio ao YouTube (precisa do ID do cliente OAuth "App para computador" no projeto do Google Cloud do usuário).
5. Teste automático de peso da live (roda alguns segundos e escolhe a configuração mais leve).
6. Resizable BAR e importar/exportar `.nip` (perfis NVIDIA); interruptor por item do driver.
7. Análise de rota por site: documento de pesquisa e melhorias.
8. Supressão de ruído por IA (RNNoise) no microfone.
9. Preencher `AppInfo.DefaultServerUrl` e a chave pública quando o servidor estiver no ar; assinatura de código do instalador.

Atualização dentro do programa: configurar `Updates:PublishToken` no servidor, `MBX_SERVER_URL` / `MBX_UPDATE_TOKEN` / `MBX_DOWNLOAD_BASE` no GitHub, e hospedar os instaladores em endereço público (ver `docs/ATUALIZACAO-NO-PROGRAMA.md`).

## Frente B: Hub MB InfoArte e landing pages
Endereços informados: Hub https://mbinfoarte.com.br/ · ArtiSys https://www.artisys.com.br/ · MB Print Lab https://mbprintlab.com.br/ (não foi possível abri-los do ambiente do assistente; faltam as descrições).
Briefing pronto: `docs/BRIEFING-SITE-AI-STUDIO.md` (também em página de cópia). O visual será feito no Google AI Studio e depois ligado ao servidor.

Pendências:
1. Confirmar a grafia da marca (programa mostra "MB InfoArt"; usuário escreveu "Infoarte") e o domínio.
2. Descrições de ArtSys, MB Lab e do quarto software (marcadas [PREENCHER] no briefing).
3. Servidor com vários produtos: campo "produto" em planos, assinaturas, licenças, chaves e afiliados; produto separado no Stripe por software; `GET /v1/app/latest?product=`.
4. E-mails transacionais: recuperação de senha e confirmação de e-mail (não existem).
5. Stripe: a conexão atual só lê. Criar os 4 preços do MB Extreme no painel (R$ 19,90 / 49,90 / 89,90 / 149,90, 1 PC) ou liberar a escrita; depois ligar os códigos `price_...` ao servidor.
6. Colocar o servidor no ar com HTTPS (`deploy/`), Google Client ID, administradores, download público.
7. Textos legais revisados por advogado; nota fiscal; prints reais para a landing.
8. Opção B (API JSON para conta dentro do site gerado): só depois do visual pronto.

## Decisões já tomadas
- Hub MB InfoArte com uma landing por software, conta única.
- O AI Studio gera o site público; login, pagamento e afiliados continuam no servidor (subdomínio de conta).
- Nada é publicado no GitHub sem o usuário dizer "pode subir".
