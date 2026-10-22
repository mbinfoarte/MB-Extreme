# Licenciamento do MB Extreme

Estrutura para vender o programa por assinatura: a pessoa cria conta no site, assina um plano e, no programa, entra com a mesma conta para ativar a licença neste PC.

> Estado atual: **modelo funcional para testes locais**. O programa, o servidor de licenças e o site básico existem e foram testados de ponta a ponta, mas o servidor ainda não está hospedado e não há gateway de pagamento ligado (ver "O que falta para vender"). Por padrão a aplicação de licença está **desligada** (beta aberto): nada é bloqueado.

## Como funciona

```
Site (conta + plano) ──► Pagamento (assinatura recorrente)
        │                        │ webhook
        ▼                        ▼
   Banco de dados  ◄──  Servidor de licenças  ◄──►  Programa (login + licença)
```

1. A pessoa cria a conta no site e assina o plano. O gateway de pagamento avisa o servidor por **webhook** (assinado com HMAC) e o servidor atualiza a assinatura.
2. No programa, **Licença > Entrar e ativar este PC** pede um código ao servidor e abre o navegador. A senha nunca passa pelo programa.
3. No site, a pessoa confere o código, entra com a conta e clica em **Aprovar**. O servidor registra o computador (respeitando o limite do plano).
4. O programa consulta o servidor e recebe uma **licença assinada** (ECDSA P-256 com SHA-256) para aquele computador.
5. O programa confere a assinatura com a **chave pública** que ele conhece, guarda a licença protegida pelo Windows (DPAPI) e libera os recursos do plano.
6. A cada 6 horas (e ao abrir, se faltar menos de 3 dias) o programa renova a licença. Sem internet, a licença guardada vale até o vencimento (7 dias, ou o fim do período pago mais 3 dias de tolerância).

### O que fica guardado no PC
Apenas as **duas últimas licenças** (a atual e a anterior), em `%AppData%\MBExtreme\license.dat`, cada uma protegida pelo Windows. Se a mais recente não valer, o programa usa a anterior. Licenças mais antigas são descartadas.

### Formato da licença
`mbx1.<conteúdo>.<assinatura>` (base64url). O conteúdo traz: conta, nome e e-mail, plano, recursos avulsos, limite de computadores, identificador do PC, emissão, vencimento da licença, **fim do período pago** (é o que o cartão do cliente mostra como "dias restantes") e situação da assinatura. O identificador do PC é um hash de um GUID do Windows com um sal; não contém nome, usuário nem dados de componentes.

### Proteções
- Licença adulterada, de outra chave ou de outro PC é recusada.
- Relógio voltado para trás (mais de 24 h em relação ao último uso) invalida até corrigir.
- Código de login com validade de 10 minutos, consulta com intervalo mínimo e confirmação no site com token anti-CSRF.
- Senhas com PBKDF2-SHA256 (210 mil iterações) e sal; mesma resposta para e-mail inexistente e senha errada; limite de tentativas por IP.
- A chave privada de assinatura é criada na primeira execução do servidor e nunca sai dele.

## Planos e recursos
A lista de páginas do plano Pro está em `src/MBExtreme.Core/Licensing/FeaturePolicy.cs` (`ProPages`). **É uma sugestão inicial; a decisão comercial é sua.** Hoje: gravação e transmissão, benchmark, antes e depois, teste de estresse, diagnóstico de latência, Modo Jogo e assistente de jogo, monitor de FPS, hardware e BIOS, receita de BIOS, analisador de rede e Wi-Fi, drivers, ajustes de desempenho, jogos e rede, e as ferramentas de arquivos grandes, duplicados e destruir arquivos. O restante é gratuito.

Para trocar o que é pago, edite essa lista. Para vender um recurso avulso, o servidor pode colocar a chave da página em `features` da licença.

## Testar localmente

Pré-requisito: .NET 8 SDK.

1. Subir o servidor (modo de teste liga a "assinatura simulada", sem cobrança):
   ```
   cd src/MBExtreme.LicenseServer
   ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5100
   ```
   No Windows (PowerShell): `$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --urls http://localhost:5100`
2. Abrir `http://localhost:5100`, criar uma conta e, em **Minha conta**, clicar em **Simular assinatura Pro**.
3. Copiar a chave pública: `http://localhost:5100/v1/public-key`.
4. No MB Extreme, abrir **Licença e conta**, preencher o endereço `http://localhost:5100` e a chave pública, marcar **Exigir licença** (opcional) e **Salvar configuração**.
5. Clicar em **Entrar e ativar este PC**, aprovar no navegador e ver o plano mudar para Pro.

Também dá para usar variáveis de ambiente no programa: `MBX_LICENSE_URL` e `MBX_LICENSE_PUBLIC_KEY`.

### Testes automáticos
- `tests/MBExtreme.Core.Tests/LicencaTests.cs`: assinatura, validade, PC errado, relógio, histórico das duas últimas, política de recursos, cliente HTTP e fluxo de login.
- `tests/MBExtreme.LicenseServer.Tests`: conta, login, ativação por código, limite de computadores, renovação, desativação, webhook de pagamento (assinatura, repetição, cancelamento), proteção contra CSRF e contra redirecionamento aberto, escape de texto, e o cliente real do programa falando com o servidor.

## Webhook de pagamento

`POST /v1/webhooks/payments` com o cabeçalho `X-Signature: sha256=<HMAC-SHA256 do corpo com o segredo>` (configure `Payments:WebhookSecret`, mínimo de 16 caracteres). Corpo:

```json
{ "id": "evt_123", "event": "subscription.activated", "email": "cliente@exemplo.com",
  "plan": "pro", "periodEnd": "2026-12-01T00:00:00Z", "providerRef": "sub_abc", "maxDevices": 2 }
```

Eventos: `subscription.activated`, `subscription.renewed`, `subscription.trialing`, `subscription.past_due`, `subscription.canceled`, `subscription.expired`. O `id` evita processar o mesmo evento duas vezes. Cada gateway (Stripe, Mercado Pago, Asaas, Paddle...) manda eventos no seu formato; o caminho recomendado é um pequeno adaptador que converte para este formato e assina.

## O que falta para vender
- **Hospedagem com HTTPS**, domínio e banco gerenciado (o modelo usa SQLite em arquivo; para escala, PostgreSQL) com backup.
- **Gateway de pagamento** e o adaptador do webhook acima. Decisões: cartão e Pix recorrente, imposto e nota fiscal (gateways "merchant of record", como Paddle e Lemon Squeezy, cuidam disso; com Stripe, Mercado Pago ou Asaas isso fica com você).
- **Confirmação de e-mail e recuperação de senha** no site (o modelo não envia e-mails).
- **Painel administrativo**: buscar cliente, conceder ou revogar plano, reembolso.
- **Chave pública fixa no programa** (hoje vem das configurações) e plano de **troca de chave**.
- **Assinatura de código** do instalador e do executável, para o SmartScreen não assustar o comprador.
- **Termos de uso e política de privacidade** (LGPD): o servidor guarda e-mail, hash da senha, nome do computador e o identificador anônimo do PC.
- Ofuscação do executável e monitoramento do servidor.

### Limite honesto da proteção
Um programa .NET pode ser aberto e alterado: nenhum bloqueio local é inviolável. O que protege de verdade é manter valor no servidor (atualizações, catálogos e bases só para quem tem licença) e ter um preço e um processo de compra justos.

## Suporte remoto (AnyDesk)
O programa tem a página **Suporte > Suporte remoto**. O cliente clica em *Preparar o AnyDesk* (baixa da fonte oficial, confere a assinatura digital do fabricante e abre), copia o ID e o pedido de suporte (com o relatório do sistema, se quiser) e envia ao suporte. O técnico digita o ID em *Sou o técnico* e o AnyDesk pede ao cliente para aceitar a conexão. O acesso nunca começa sem o aceite do cliente, e o programa não configura acesso sem supervisão.

Observações: o AnyDesk é de terceiros e proprietário; o atendimento comercial por quem presta o suporte exige licença do AnyDesk. Próxima etapa (depende do servidor no ar): enviar o pedido direto ao servidor, listar no painel do técnico os clientes disponíveis e registrar o histórico de atendimentos.
