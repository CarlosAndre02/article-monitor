# 0005. Regra de parada da descoberta comum a todas as fontes

- **Status:** Aceito
- **Data:** 2026-11-10

## Contexto

Cada coleta precisa decidir até onde ler a listagem de uma fonte. Ler de menos perde artigos, e ler demais faz requisições inúteis. Os casos difíceis são estes:
- banco zerado;
- uma lacuna maior que uma página da listagem;
- matérias antigas fixadas no topo;
- artigos sem data;
- execuções interrompidas no meio.

## Decisão

- O adapter só pagina: entrega os artigos do mais novo para o mais antigo e busca as páginas sob demanda.
- A `DiscoveryStopPolicy`, na Application, decide quando parar, igual para todas as fontes:
  - pelo teto de itens por execução;
  - por uma sequência de artigos já conhecidos;
  - pelo limite de data, que é a marca d'água menos uma margem.
- Só contam na sequência os conhecidos publicados até a marca d'água, ou sem data, porque a marca d'água só avança quando uma execução é concluída.
- Quando a execução pode ter perdido artigos, ela é marcada como possível lacuna.

As regras em detalhe estão em [project.md](../project.md#regra-de-parada-da-descoberta).

## Alternativas consideradas

- **Cada adapter decide quando parar**: a regra ficaria duplicada e divergiria entre fontes.
- **Parar no primeiro artigo conhecido**: uma matéria antiga fixada no topo encerraria a coleta antes dos artigos novos.
- **Parar só pela data**: não funciona com artigos sem data.
- **Contar qualquer conhecido na sequência**: uma execução interrompida já gravou os artigos mais novos. A execução seguinte pararia neles e nunca coletaria os mais antigos que a interrompida não alcançou.

## Consequências

- O discoverer de toda fonte precisa entregar os artigos do mais novo para o mais antigo e paginar sob demanda. Os testes de contrato verificam isso.
- Fontes sem data dependem só da sequência de conhecidos e do teto.
- Uma execução que para no teto também avança a marca d'água, então os artigos além do teto não são buscados depois. A execução fica marcada como possível lacuna.
