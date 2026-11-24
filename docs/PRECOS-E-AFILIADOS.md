# Preços e programa de afiliados (levantamento e recomendação)

Pesquisa de 23/10/2026. **Os preços de concorrentes vêm de buscas e de páginas de terceiros (lojas, cupons, comparativos), com promoções que variam; confirme nos sites oficiais antes de decidir.** As taxas de pagamento e impostos abaixo são **hipóteses para o cálculo**, não valores confirmados: confira no painel do seu gateway e com seu contador.

## 1. O que o mercado cobra (referências encontradas)
| Produto | Tipo | Preço encontrado |
|---|---|---|
| ExitLag | Redutor de ping (assinatura) | cerca de R$ 22/mês (1 mês, listagem de loja) e ~R$ 11,60/mês no anual promocional |
| NoPing | Redutor de ping | faixa de US$ 6 a 25/mês conforme a fonte (valores divergem) |
| IObit Advanced SystemCare Pro | Otimizador genérico | cerca de US$ 15 a 30 por ano (1 PC), com promoções |
| CCleaner Pro | Limpeza/otimização | cerca de US$ 30 a 45 por ano |
| Programas completos de manutenção (ex.: CleanMyPC) | Otimizador premium | US$ 180 a 200 por ano (2 a 5 PCs) |

Leitura: **quem vende redução de ping/jogos cobra por mês (R$ 20 a 25)**; **otimizadores genéricos cobram por ano (equivalente a R$ 7 a 20 por mês)**. O MB Extreme junta as duas coisas (otimização, jogos, FPS, rede, gravação, acesso remoto), então um preço mensal na faixa do ExitLag com descontos fortes nos períodos longos é coerente.

## 2. Preços recomendados (1 computador)
| Plano | Total | Equivale a | Desconto |
|---|---|---|---|
| Mensal | R$ 19,90 | R$ 19,90/mês | — |
| Trimestral | R$ 49,90 | R$ 16,63/mês | 16% |
| Semestral | R$ 89,90 | R$ 14,98/mês | 25% |
| Anual | R$ 149,90 | R$ 12,49/mês | 37% |
Computador extra: **30% mais barato** que o primeiro (já no sistema). Teste grátis: 3 dias.

**Por quê:** o mensal fica um pouco abaixo do ExitLag (produto mais estreito); o anual (R$ 149,90) fica na faixa de CCleaner/IObit em reais, mas entrega mais; os descontos empurram para períodos longos, que reduzem cancelamento e taxa por transação. É um ponto de partida: **teste por 30 a 60 dias e ajuste** (variáveis em `Billing:Prices:*`, sem recompilar; lembre de criar os mesmos valores no Stripe).

Alternativa de lançamento (para ganhar base): mensal R$ 14,90 nos primeiros meses, voltando a R$ 19,90 para novos clientes depois.

## 3. Conta de margem (hipóteses: taxa de pagamento 5% e imposto 6% sobre o valor pago)
| Plano | Cobrado | Taxa+imposto | Líquido | Comissão 20% | Sobra | Comissão 35% | Sobra |
|---|---|---|---|---|---|---|---|
| Mensal | R$ 19,90 | R$ 2,18 | R$ 17,71 | R$ 3,98 | R$ 13,73 | R$ 6,96 | R$ 10,75 |
| Trimestral | R$ 49,90 | R$ 5,49 | R$ 44,41 | R$ 9,98 | R$ 34,43 | R$ 17,46 | R$ 26,95 |
| Semestral | R$ 89,90 | R$ 9,89 | R$ 80,01 | R$ 17,98 | R$ 62,03 | R$ 31,46 | R$ 48,55 |
| Anual | R$ 149,90 | R$ 16,49 | R$ 133,41 | R$ 29,98 | R$ 103,43 | R$ 52,47 | R$ 80,95 |

- Mesmo no nível Ouro (35%), sobra mais de **50% do valor cobrado**, o que cobre servidor, suporte e desenvolvimento. Guias de afiliados de software costumam recomendar não passar de 30% a 40% da margem bruta e comissões recorrentes de 20% a 30% (com programas de topo até 40%).
- **Ponto de equilíbrio da infraestrutura:** com servidor/site em torno de R$ 150/mês, bastam cerca de **9 assinaturas mensais** para pagá-lo. A rede de rotas (relays) é um custo à parte, só se você decidir fazer.
- Se o imposto for maior (Simples Nacional faixas altas) ou a taxa for maior (parcelamento no cartão), a sobra cai; refaça a conta com os números reais.

## 4. Programa de afiliados (como está implementado)
| Nível | Clientes pagantes confirmados | Comissão |
|---|---|---|
| Latão | 0+ | 20% |
| Bronze | 5+ | 25% |
| Prata | 20+ | 30% |
| Ouro | 50+ | 35% |
- **Recorrente por 12 meses** (cada pagamento do cliente indicado), sobre o valor **efetivamente pago**; pagamento zero (teste) não gera comissão.
- **Cliente "confirmado"** = indicado com pagamento que já passou da carência. O nível usado em cada comissão é o do momento do pagamento.
- **Carência de 30 dias** (cobre o arrependimento de 7 dias, estornos e *chargebacks*). **Estorno total** do pagamento cancela a comissão.
- **Cookie de 60 dias**, vale a primeira indicação (não se troca de afiliado depois); **auto-indicação bloqueada**.
- **Saque por Pix**, mínimo de R$ 50, pedido no painel, conferido e pago por você (API admin). Tudo configurável em `Affiliates:*`.
- **Riscos a vigiar:** contas falsas só para ganhar comissão (por isso a comissão vem de pagamento real e tem carência), spam e anúncios com a sua marca (as regras públicas proíbem), e estornos depois do saque (acerto manual: a comissão fica marcada como estornada).
- **Imposto do afiliado:** pagar pessoa física pode exigir recibo/retenção; combine com seu contador.

Ajustes possíveis depois de medir: comissão só no 1º pagamento (reduz custo), desconto para quem entra pelo link, bônus por meta mensal, código personalizado.
