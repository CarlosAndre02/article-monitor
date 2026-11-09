# Projeto

## Premissas

Revisão das premissas acima, com o que foi discutido depois. Ainda são premissas a validar.

### Escopo

- Bot de Discord que coleta artigos de portais de notícias várias vezes ao dia, calcula a relevância (heat) de cada artigo para cada palavra-chave cadastrada e permite consultar os resultados pelo Discord.
- Os resultados são consultados sob demanda, por slash commands.
- Uso pessoal, com volume estimado de ~1.000 artigos/dia.

### Arquitetura

- Três processos e um banco:
    - **Worker + scheduler**: coleta, extrai, gera embedding e pontua. Também roda a limpeza diária de retenção.
    - **API**: cadastro de palavras e nuvens, consulta por heat mínimo e registro de feedback.
    - **Bot Discord**: cliente da API, sem regra de negócio. Trocar a interface (web, Telegram) não exige mudar worker nem API.
    - **PostgreSQL**: único ponto de contato entre worker e API.
- Endereços e segredos (banco, URL da API, token do Discord, chave do LLM) vêm de variáveis de ambiente, para que cada processo rode local ou na nuvem sem mudar código.
- A API exige autenticação (chave compartilhada com o bot), porque fica exposta quando bot e API rodam em lugares diferentes.

### Coleta

- Adapters por função, não por site:
    1. **Descoberta**: adapter genérico de RSS/Atom. Cada fonte é uma lista de URLs de feed (ex.: feeds do G1 por editoria).
    2. **Extração**: extrator genérico (trafilatura em Python ou @mozilla/readability em Node).
    3. **Específico por site** (HTML/CSS): só onde o extrator genérico falhar.
    4. **API oficial** para fontes com paywall (NYT: Times Newswire e Article Search), trazendo título, abstract e lead.
- Raspar conteúdo atrás de paywall viola os termos de uso. Por isso, artigo com conteúdo parcial é um caso normal do modelo de dados, não exceção.
- Intervalo de coleta configurável por fonte.
- Para não ser bloqueado: respeitar o `robots.txt`, enviar User-Agent identificável, usar `ETag`/`If-Modified-Since` nos feeds e aplicar rate limit por domínio.
- Cada execução por fonte é registrada (status, artigos novos, erro). Sem isso, um feed quebrado ou um extrator que parou de funcionar passa despercebido.
- Fontes são coletadas em paralelo dentro do único worker, com limite de concorrência configurável e timeout por fonte. O ganho vem de ler domínios diferentes ao mesmo tempo, porque dentro de um domínio vale o rate limit. A coleta espera quase sempre pela rede, então vários processos não trariam ganho.

### Armazenamento e deduplicação

- Todo artigo coletado é salvo: URL canônica, título, fonte, data de publicação, idioma, texto, `tsvector` e embedding.
- Deduplicação pelo hash da URL canônica, sem parâmetros de rastreamento (`utm_*`), já que o mesmo artigo aparece em mais de um feed. Com coleta em paralelo, a garantia vem de um índice único nesse hash, e a inserção trata o conflito como "já coletado".
- O matching é uma etapa separada da coleta, o que permite:
    - match retroativo ao cadastrar uma palavra nova;
    - reprocessar ao mudar o algoritmo de score ou editar a nuvem de uma palavra.
- O heat pertence ao par artigo × palavra-chave, não ao artigo. Cada par guarda o heat e os termos que deram match (ex.: "heat 65%: IPCA no título, Copom, juros").

### Matching

- O full-text search do PostgreSQL resolve acentos e caixa, limite de palavra ("IA" não bate em "média") e stemming ("eleição" e "eleições").
- Combinar `unaccent` com o dicionário `portuguese` exige uma configuração de busca customizada; ela não vem pronta.
- Fontes em mais de um idioma (G1 em português, NYT em inglês): o `tsvector` usa a configuração do idioma do artigo, e a nuvem de termos inclui termos em cada idioma monitorado. O embedding multilíngue cobre a semântica entre idiomas.
- Ambiguidade de siglas ("IA" e o verbo "ia" ficam iguais sem caixa) é responsabilidade de quem cadastra a palavra, via termos negativos.
- Entradas vindas do Discord (palavras, termos, pesos) são normalizadas e validadas antes de chegar ao banco.

### Termômetro de relevância (heat)

- A regra "1 termo da nuvem = salva com pelo menos 10%" não funciona: termos genéricos geram falso positivo em massa, e match exato nem sempre é relevante (um "leia também" no rodapé, palavras ambíguas como "Apple").
- Nuvem de termos gerada por LLM uma vez, no cadastro da palavra, salva no banco e editável pelo Discord:

| Tipo de termo | Exemplo para "inflação" | Peso |
| --- | --- | --- |
| Sinônimo forte | IPCA | 0,8 |
| Relacionado | Copom, juros | 0,5 |
| Genérico | economia | 0,1 ou fora |
| Negativo (desambiguação) | para "Apple": torta, fruta | exclui |

- A resposta do LLM é validada contra um schema (termos, pesos entre 0 e 1, negativos) antes de ser salva, porque o LLM pode devolver formato inválido.
- Score lexical com mais sinais do que presença: posição do termo (título > lead > corpo), frequência e quantidade de termos **distintos** da nuvem. Três termos diferentes valem mais que um repetido cinco vezes.
- Embedding de todos os artigos, gerado na coleta por um modelo local multilíngue (~384 dimensões) e guardado com pgvector. A similaridade de cosseno entre o artigo e a descrição do tema compõe o heat.
- Pipeline:
    1. Match exato da palavra no título ou lead → heat alto direto.
    2. Score lexical ponderado + similaridade por embedding → heat.
    3. Opcional: um LLM barato classificando "relevante ou não" nos casos limítrofes.
- Nada é descartado na ingestão: o corte é parâmetro da consulta (`/buscar inflação min:40`), então um limiar mal escolhido não perde dados.
- Feedback 👍/👎 nos artigos retornados pelo `/buscar`, salvo para recalibrar pesos e limiar com dados reais. Botões são preferíveis a reações, porque chegam ao bot como interação.

### Interface no Discord

- Discord como interface: canais e threads organizam o histórico, e slash commands e embeds cobrem a interação. Telegram não tem canais para organizar por tema; no WhatsApp, a API oficial é voltada a empresas e bibliotecas não oficiais arriscam banimento.
- Slash commands, que dispensam o intent privilegiado de Message Content:

| Comando | O que faz |
| --- | --- |
| `/palavra add` | Cadastra uma palavra-chave e gera sua nuvem |
| `/palavra listar` | Lista as palavras cadastradas |
| `/palavra nuvem` | Mostra e edita os termos e pesos da nuvem |
| `/buscar <palavra> min:<heat>` | Lista artigos acima do limiar |

- Cada resultado traz link, título, heat, termos que deram match e um trecho curto. O texto completo do artigo nunca é postado (direitos autorais).
- O bot se conecta pelo Gateway (WebSocket de saída): roda local ou na nuvem sem IP público nem porta aberta. Trocar é parar o processo num lugar e subir no outro.
- Duas applications no Discord Developer Portal, uma de produção e uma de desenvolvimento, cada uma com seu token. Duas instâncias com o mesmo token recebem os mesmos eventos e respondem em duplicidade.
- O servidor do Discord é configuração, não código: pode receber outros bots, cada um no seu repositório. Nomes de comando específicos evitam conflito com comandos de outros bots no mesmo servidor.

### Retenção

- Meta: banco com no máximo 1 GB.
- Estimativa por artigo, a confirmar com dados reais: metadados ~0,5 KB, texto comprimido ~3 KB, `tsvector` ~2–3 KB, embedding ~1,5 KB. Total ~8–10 KB, ou ~300 MB/mês sem retenção.
- Retenção por tempo, não pelos "N últimos registros", porque as fontes têm volumes muito diferentes:

| Camada | O que fica | Por quanto tempo |
| --- | --- | --- |
| Relevantes (heat acima do limiar ou com 👍) | Tudo | Para sempre |
| Demais, dentro da janela | Texto, `tsvector`, embedding e metadados | 60 dias; é onde o match retroativo funciona |
| Demais, após a janela | Só metadados (~15 MB/mês) | Até a deduplicação deixar de precisar deles |

- A janela de 60 dias ocupa ~480–600 MB, o que deixa margem para a meta de 1 GB.
- Metadados mantidos para sempre crescem ~180 MB/ano e acabam estourando a meta. A deduplicação só precisa do hash enquanto o artigo pode reaparecer num feed, então metadados antigos podem ser apagados.
- Após a janela, o match retroativo de uma palavra nova só alcança os metadados (título).
- A limpeza é um job diário no scheduler. O limiar de relevância vem de configuração, não fixo no SQL, e artigos com 👍 são preservados. O autovacuum reaproveita o espaço liberado.

### Decisões em aberto

- Stack (Python, Node ou .NET): define extrator, scheduler e biblioteca do bot. A opção .NET está avaliada em [stack-dotnet.md](stack-dotnet.md).
- Provedor do LLM e modelo de embedding.
- Hospedagem (VPS, servidor local, Docker).
- Fontes e feeds iniciais.
- Por quanto tempo manter os metadados após a janela de retenção.
- Como combinar score lexical e similaridade por embedding no heat.
