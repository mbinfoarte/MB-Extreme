# Política de privacidade (rascunho)

> **Rascunho técnico para revisão de um advogado antes de publicar.** Descreve o que o MB Extreme faz hoje. Atualize ao mudar o comportamento. Referência: LGPD (Lei 13.709/2018).

**Controlador:** MB InfoArt (preencher razão social, CNPJ e e-mail do encarregado de dados).

## O que fica só no seu computador
Configurações, backups de ajustes, histórico de sessões de jogo e de gargalos, biblioteca de jogos, gravações, chave de transmissão (protegida pelo Windows) e a licença. Nada disso sai do PC.

## O que o servidor recebe
| Dado | Para quê | Quando |
|---|---|---|
| E-mail, nome e senha (guardada só como hash PBKDF2) | Conta e login no site | Ao criar a conta |
| Identificador do PC (hash de um GUID do Windows com sal) e nome do computador | Limite de computadores do plano e teste grátis | Ao ativar a licença |
| Assinatura: plano, situação, validade, quantidade de PCs; no pagamento, os identificadores do cliente no Stripe | Liberar recursos e cobrar | Ao assinar |
| Pagamento (cartão) | **Tratado pelo Stripe**; o MB Extreme nunca vê nem guarda o cartão | Ao assinar |
| Suporte remoto: ID do PC, quem pediu acesso, quando, se foi aceito, volume de tráfego | Segurança e auditoria | Ao usar o acesso remoto |

O conteúdo do acesso remoto (tela, mouse, teclado) é **cifrado de ponta a ponta**; o servidor apenas repassa bytes e não consegue ler.

## O que só é enviado se você autorizar
Resumo **anônimo** de sessões de jogo (nome do executável do jogo, faixa de hardware, faixa de RAM, FPS médio e mínimos, tipo de gargalo, duração). Desligado por padrão, em *Monitor de gargalos*. Não inclui nome, e-mail, programas instalados nem arquivos. *(O envio ainda não está implementado; hoje o resumo fica só no PC.)*

### Ranking anônimo (desligado por padrão)
Se você ligar a opção "Participar do ranking anônimo", o app envia, depois de cada benchmark ou medição de jogo: o modelo do processador e da placa de vídeo, a quantidade de memória e de núcleos, as notas e os FPS medidos, o nome do jogo (só nas medições de jogo), a versão do app e um **código aleatório** criado no seu PC. **Não enviamos** nome, e-mail, número de licença, endereço de IP guardado, lista de programas nem arquivos. No servidor, o código é guardado já embaralhado (hash com um segredo nosso), e cada código mantém apenas a medição mais recente de cada tipo. Os dados servem para mostrar a você em que posição o seu PC fica e as medianas de PCs parecidos; medições com mais de 18 meses são apagadas. Você pode ver exatamente o que será enviado, desligar a opção quando quiser e usar "Apagar meus dados do ranking", que remove tudo ligado ao seu código e cria um novo.

## Seus direitos
Acessar, corrigir, exportar e **excluir** seus dados, revogar consentimentos e remover computadores em *Minha conta*. Para exclusão da conta, escreva para o e-mail do encarregado. Guardamos dados fiscais pelo prazo legal.

## Retenção e segurança
Auditoria de suporte: guardar por 12 meses (ajustar). Senhas com PBKDF2-SHA256; licença assinada (ECDSA); tráfego por HTTPS; limite de tentativas. Em caso de incidente, notificaremos a ANPD e os afetados conforme a LGPD.

## Terceiros
Stripe (pagamentos), provedor de hospedagem do servidor (preencher), Microsoft (winget, Windows Update, quando você usa essas funções).
