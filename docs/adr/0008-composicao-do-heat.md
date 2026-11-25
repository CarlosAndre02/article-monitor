# 0008. Heat composto por score lexical e similaridade por embedding

- **Status:** Proposto
- **Data:** 2026-11-08

## Contexto

O heat mede a relevância de um artigo para uma palavra-chave ([requirements.md](../requirements.md#r5-relevância-heat)). Uma regra simples, como "1 termo da nuvem = pelo menos 10%", gera falsos positivos em massa com termos genéricos. E nem todo match exato é relevante: um "leia também" no rodapé, ou palavras ambíguas como "Apple".

## Decisão

- Cada palavra tem uma nuvem de termos com pesos ([ADR 0007](0007-nuvem-de-termos-no-worker.md)):

| Tipo de termo | Exemplo para "inflação" | Peso |
| --- | --- | --- |
| Sinônimo forte | IPCA | 0,8 |
| Relacionado | Copom, juros | 0,5 |
| Genérico | economia | 0,1 ou fora |
| Negativo (desambiguação) | para "Apple": torta, fruta | exclui |

- O score lexical considera mais que presença: a posição do termo (título > lead > corpo), a frequência e a quantidade de termos **distintos** da nuvem. Três termos diferentes valem mais que um repetido cinco vezes.
- Todo artigo recebe um embedding de título e lead, gerado na coleta por um modelo local multilíngue. A similaridade de cosseno com a descrição do tema compõe o heat.
- O pipeline tem três passos:
  1. Um match exato da palavra no título ou no lead dá heat alto direto.
  2. Nos demais casos, o heat combina o score lexical ponderado e a similaridade por embedding.
  3. Opcionalmente, um LLM barato classifica os casos limítrofes como relevantes ou não.
- O heat só é gravado para pares artigo × palavra com score acima de zero.

## Alternativas consideradas

- **Um termo da nuvem garante um heat mínimo**: falsos positivos em massa com termos genéricos.
- **Só match exato**: perde artigos relevantes que usam sinônimos ou termos relacionados, e conta matches irrelevantes.

## Consequências

- Como combinar o score lexical e a similaridade segue em aberto ([requirements.md](../requirements.md#decisões-em-aberto)). O feedback 👍/👎 fornece os dados para calibrar pesos e limiar.
- O embedding usa título e lead, e não o texto inteiro, para que artigos com conteúdo parcial fiquem comparáveis aos demais.
- O embedding de todos os artigos ocupa espaço na janela de retenção: ~1 KB por artigo com 256 dimensões.
- Gravar só os pares com score acima de zero evita multiplicar linhas sem uso.
