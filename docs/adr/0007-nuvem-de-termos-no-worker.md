# 0007. Nuvem de termos gerada pelo worker

- **Status:** Proposto
- **Data:** 2026-11-08

## Contexto

A nuvem de termos de uma palavra-chave é gerada por um LLM uma vez, no cadastro, e depois pode ser editada. Um LLM local leva de 30 a 90 segundos por nuvem, e o Discord exige que um slash command seja reconhecido em 3 segundos.

## Decisão

- A API grava a palavra como pendente e não espera o LLM.
- O worker gera a nuvem, valida a resposta contra um schema (termos, pesos entre 0 e 1, negativos) e roda o match retroativo.
- O `/palavra add` responde de forma adiada (defer) e consulta o status da palavra na API antes de dar o retorno.
- O LLM é acessado por `IChatClient` (`Microsoft.Extensions.AI`), então trocar entre local (Ollama) e nuvem é configuração.

## Alternativas consideradas

- **A API chama o LLM durante a requisição**: era a premissa inicial. Com um LLM local, estoura o timeout da requisição.

## Consequências

- A palavra só fica utilizável depois que o worker a processa.
- A resposta do LLM é validada mesmo com saída estruturada, porque ele pode devolver um formato inválido.
- O provedor do LLM segue como decisão em aberto ([requirements.md](../requirements.md#decisões-em-aberto)).
