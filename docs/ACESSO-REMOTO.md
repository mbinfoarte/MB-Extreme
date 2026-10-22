# Acesso remoto próprio do MB Extreme

> **Versão 2 (estilo AnyDesk).** Página única **Acesso remoto**: "Este computador" (seu ID, copiar, permitir/bloquear acesso) e "Outro computador" (digite o ID e *Conectar*), com **tela, mouse, teclado e som**, e lista de conexões recentes. **Qualquer usuário do app** pode conectar a outro PC (precisa estar com a conta ativa em *Licença e conta*; a conta grátis serve); o outro lado **sempre aceita** na tela. Por padrão **basta o ID**: os dois lados veem um código de segurança de 6 dígitos que devem conferir (se diferirem, encerre). Opcionalmente o dono do PC liga *Exigir senha* (mais seguro). O som do PC do cliente chega ao técnico (PCM 16 bits mono 22 kHz). O texto abaixo da versão 1 vale com essas diferenças.

Permite que o técnico (conta autorizada) atenda o computador de um cliente pelo próprio MB Extreme, com ID numérico, sem programa de terceiros. O AnyDesk continua como opção secundária na página Suporte remoto.

## Como funciona

1. **Cliente**: Suporte remoto > *Ligar o acesso remoto*. O app registra o PC no servidor (`POST /v1/remote/register`) e recebe um **ID de 9 dígitos** e um **segredo** que fica protegido pelo Windows (DPAPI). Mostra o ID e uma **senha de uso único** (10 caracteres, muda a cada sessão).
2. **Quem conecta**: Acesso remoto > digita o ID > *Conectar* (senha só se o dono pediu). Exige uma licença assinada do servidor (conta com login). Se `Remote:Technicians` estiver preenchido no servidor, só essas contas conectam; vazio, qualquer conta. `Admin:Emails` vê a auditoria.
3. O servidor avisa o cliente (“o técnico X quer acessar”). O cliente **aceita ou recusa** numa janela. Sem resposta em 60 s, cancela.
4. Se aceito, os dois lados trocam chaves (ECDH P-256 autenticado por HMAC com a senha) e abrem um canal **AES-256-GCM** com contador (repetição e reordenação são recusadas).
5. O cliente envia a tela (JPEG, até 1280 px, 8 quadros/s, só quando muda) e aplica mouse/teclado vindos do técnico. Aparece uma **faixa vermelha “Sessão remota ativa”** com botão Encerrar.
6. O servidor só **repassa bytes cifrados**. Registra auditoria: quem, quando, se aceitou, tráfego, resultado (`GET /v1/remote/audit`, só técnicos).

## Segurança

- Só o cliente inicia; aceite a cada sessão; sem acesso sem supervisão.
- A senha nunca vai ao servidor. Quem não a conhece (inclusive o servidor) não completa o acordo de chaves.
- Teclas bloqueadas: Windows. Limite de mensagem 6 MB, de sessão 4 h (`Remote:MaxSessionMinutes`), de tentativas por IP.
- IDs são só endereços: conhecer um ID não dá acesso (precisa de conta de técnico, aceite e senha).

## Configuração do servidor (appsettings)
```
"Remote": { "Technicians": "voce@seudominio.com;outro@seudominio.com", "MaxSessionMinutes": 240 }
```
Sem `Technicians`, o lado do técnico fica desligado (404). Use HTTPS na hospedagem (o app usa `wss://` quando o endereço é `https://`).

## O que está pronto e testado (neste ambiente Linux)
- Protocolo, criptografia e IDs (testes unitários).
- Servidor de sinalização/relay/auditoria e **sessão ponta a ponta** entre agente e técnico pelo servidor real (testes de integração).
- Captura (GDI), injeção (SendInput), faixa de sessão e console do técnico: **compilam, mas não foram executados no Windows**. Precisam de teste manual em duas máquinas.

## O que NÃO está pronto (sub-etapas do plano)
- **5.4** Ctrl+Alt+Del, área de transferência e atalhos especiais.
- **5.5** Serviço do Windows assinado para controlar janelas de administrador (UAC) e a tela de login. Sem ele, essas janelas não são controláveis.
- **5.6** Conexão direta (STUN/TURN), vídeo H.264 adaptativo, transferência de arquivos e chat. Hoje tudo passa pelo servidor (gasta banda dele) com JPEG.
- **5.7** Teste de segurança externo e verificação em dois passos para as contas de técnico.
- Troca do acordo por senha (HMAC) por um PAKE (SPAKE2/OPAQUE), que impede tentativa de adivinhar a senha offline por um servidor malicioso.
- Assinatura de código do executável (antivírus costumam marcar ferramentas de acesso remoto).
