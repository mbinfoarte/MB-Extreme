# Lista de lançamento (Etapa 7)

Legenda: ✅ feito e verificado · 🟡 feito, falta teste no Windows real · ⛔ depende de você / de terceiros

## Código e testes
- ✅ Testes do Core (xUnit) e do servidor (WebApplicationFactory) a cada push, no CI.
- ✅ Revisões independentes feitas nas rodadas anteriores; esta rodada (etapas 1–8) ainda **não** passou por revisão independente: rodar `/code-review` antes de abrir ao público.
- 🟡 Interface (painel, estúdio, biblioteca, gargalos, suporte, console do técnico): compila, mas nenhuma tela foi aberta num Windows real nesta rodada.
- 🟡 Captura de tela, SendInput, ETW (FPS), contadores de desempenho, registro (prioridade por jogo): dependem do Windows real.

## Desempenho (não pesar no jogo)
- Estúdio: codificação na placa de vídeo (NVENC/AMF/QSV); pré-visualização reduzida (480 px) e só quando o estúdio está parado.
- Gargalos: 1 amostra por segundo; o analisador processa 100 mil amostras em milissegundos (teste automático).
- ⛔ Medir no seu PC: FPS com e sem estúdio/gargalos ligados (Antes e depois).

## Assinatura de código
- ✅ O CI assina o aplicativo e o instalador **se** os segredos `SIGN_PFX_BASE64` (certificado .pfx em base64) e `SIGN_PFX_PASSWORD` existirem. Sem eles, publica sem assinatura (como hoje).
- ⛔ Comprar o certificado (OV ou EV; EV evita o alerta do SmartScreen desde o primeiro dia). Ferramentas de acesso remoto costumam ser marcadas por antivírus: assinar e enviar o executável para análise prévia (Microsoft, principais antivírus).
- Ofuscação: **não adotada**. Em .NET ela atrapalha a depuração e dá pouca proteção real; a licença é verificada por assinatura ECDSA, não por segredo no código.

## Servidor
- ⛔ Hospedar com HTTPS, definir `Remote:Technicians`, `Admin:Emails`, `Stripe:*`, `Billing:*`; fazer backup da pasta `data` (banco e chave de assinatura). Perder a chave invalida todas as licenças.
- ⛔ Fixar a URL do servidor e a chave pública no app de venda (hoje configuráveis).
- ⛔ Teste de segurança externo do servidor e do acesso remoto; verificação em dois passos para contas de técnico e de administrador.

## Jurídico
- 🟡 `POLITICA-PRIVACIDADE.md` e `TERMOS-DE-USO.md` são rascunhos: revisar com advogado, preencher CNPJ, foro e encarregado.

## Antes de abrir ao público
1. Revisão de código independente desta rodada. 2. Teste manual das telas novas. 3. Teste do acesso remoto em duas máquinas. 4. Teste do Stripe em modo de teste. 5. Certificado e assinatura. 6. Textos legais revisados.
