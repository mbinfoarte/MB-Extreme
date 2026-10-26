# Status do projeto (duas frentes separadas)

Atualizado em 26/10/2026. Use este arquivo para retomar o trabalho sem depender do histórico da conversa.

## Frente A: Aplicativo MB Extreme (Windows, .NET 8, WPF)
**Publicado até o beta-38.** Em commits locais, ainda sem subir: atualização dentro do programa (`d3741d8`).

Pendências, em ordem sugerida:
1. Testar o beta-38 no PC: Modo leve da live, Otimizar a live, vídeo ao vivo das câmeras (inclusive celular por endereço de rede), áudio profissional, bolinha REC, grupo NVIDIA da biblioteca, capas com SteamGridDB.
2. Tela de Início menor (precisa de prints do usuário) e reorganização do layout do estúdio.
3. Estúdio: cenas salvas com "Transmitir agora" (modo estúdio real exige compositor próprio; ver `docs/PESQUISA-ESTUDIO-OBS.md`).
4. Teste automático de peso da live (roda alguns segundos e escolhe a configuração mais leve).
5. Resizable BAR e importar/exportar `.nip` (perfis NVIDIA); interruptor por item do driver.
6. Análise de rota por site: documento de pesquisa e melhorias.
7. Supressão de ruído por IA (RNNoise) no microfone.
8. Preencher `AppInfo.DefaultServerUrl` e a chave pública quando o servidor estiver no ar; assinatura de código do instalador.

Atualização dentro do programa: configurar `Updates:PublishToken` no servidor, `MBX_SERVER_URL` / `MBX_UPDATE_TOKEN` / `MBX_DOWNLOAD_BASE` no GitHub, e hospedar os instaladores em endereço público (ver `docs/ATUALIZACAO-NO-PROGRAMA.md`).

## Frente B: Hub MB InfoArte e landing pages
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
