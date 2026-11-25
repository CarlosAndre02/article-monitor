# 0002. Worker, API e bot como processos separados, com o banco entre worker e API

- **Status:** Proposto
- **Data:** 2026-11-08

## Contexto

O sistema coleta e pontua periodicamente, e o usuário consulta sob demanda pelo Discord. A interface pode mudar (web, Telegram), e cada processo precisa rodar local ou na nuvem.

## Decisão

- **Worker**: agenda, coleta, extração, embedding, score e a limpeza diária de retenção.
- **API**: cadastro de palavras e nuvens, consulta por heat mínimo e registro de feedback.
- **Bot do Discord**: cliente da API, sem regra de negócio. Não referencia o núcleo nem a infraestrutura, só os DTOs da API.
- **SQL Server**: o único ponto de contato entre worker e API.
- Endereços e segredos (banco, URL da API, token do Discord, chave do LLM) vêm de variáveis de ambiente.
- A API exige autenticação por uma chave compartilhada com o bot, porque fica exposta quando os dois rodam em lugares diferentes.

## Alternativas consideradas

- **Vários processos de coleta**: não trariam ganho. A coleta espera quase sempre pela rede, e dentro de um domínio vale o rate limit. O paralelismo entre fontes dentro do único worker já aproveita a espera.

## Consequências

- Trocar a interface não exige mudar o worker nem a API.
- O bot se conecta pelo Gateway do Discord, uma conexão de saída, então roda local ou na nuvem sem IP público nem porta aberta.
- São duas applications no Discord Developer Portal, uma de produção e uma de desenvolvimento, cada uma com seu token. Duas instâncias com o mesmo token recebem os mesmos eventos e respondem em duplicidade.
- Se um dia houver várias instâncias do worker, cada uma reserva as fontes devidas com um `UPDATE` atômico (`UPDLOCK, READPAST`), com prazo de validade. Isso dispensa uma fila.
