# 0003. Agenda global em horários fixos, com recuperação ao iniciar

- **Status:** Aceito
- **Data:** 2026-11-10

## Contexto

O worker roda principalmente numa máquina pessoal, que pode ficar desligada ou suspensa. Cerca de 6 coletas por dia bastam: os feeds do G1 trazem 100 itens, que cobrem de 5 a 15 dias de publicações, então coletar a cada 4 horas não perde nada. A premissa inicial era um intervalo por fonte, com a próxima execução de cada uma guardada numa tabela.

## Decisão

- Horários fixos e globais, configurados em `Collector:Schedule`. O padrão é a cada 4 horas, de 00:00 a 20:00, em `America/Sao_Paulo`.
- Ao iniciar, uma fonte que perdeu um horário com a máquina desligada coleta uma vez, na hora. Vários horários perdidos viram uma só coleta.
- O relógio de parede é conferido a cada minuto.

As regras em detalhe estão em [project.md](../project.md#agenda).

## Alternativas consideradas

- **Intervalo por fonte, com a próxima execução numa tabela**: mais flexível, mas sem necessidade para o volume das fontes, e horários fixos tornam previsível quando a coleta roda.
- **Um timer longo até o próximo horário** (`Task.Delay` de horas): no Linux, o timer para de contar enquanto a máquina está suspensa e dispara atrasado depois.
- **Quartz ou Hangfire**: resolvem agendamento, mas são excesso para um único ciclo periódico.

## Consequências

- Todas as fontes seguem os mesmos horários. Uma fonte de volume muito maior exigiria mais horários para todas, ou rever esta decisão.
- Na nuvem, o mesmo código roda sem mudança. Como a máquina não desliga, a recuperação nunca acontece.
- Uma máquina desligada por mais tempo do que o feed cobre perde artigos. A execução seguinte fica marcada como possível lacuna ([ADR 0005](0005-regra-de-parada-comum.md)).
