# Coleta de artigos

Nos horários marcados, o worker coleta os artigos novos de cada fonte. Ele lista os artigos, descarta os que já conhece, lê o texto dos novos, grava cada um e registra a execução.

- **Requisitos atendidos:** [R1, R2 e R3](../requirements.md#requisitos).
- **Decisões:** [ADR 0003](../adr/0003-agenda-global-com-recuperacao.md) (agenda), [0004](../adr/0004-fontes-como-modulos-em-um-projeto.md) (módulos de fonte), [0005](../adr/0005-regra-de-parada-comum.md) (regra de parada) e [0006](../adr/0006-matching-na-aplicacao.md) (texto comprimido).
- **Fontes:** cada fonte só sabe listar artigos e ler o texto de uma página ([sources.md](../sources.md)). As etapas abaixo são iguais para todas.
- **Modelo de domínio:** as invariantes de cada objeto estão no [project.md](../project.md). Aqui ficam as regras que coordenam vários objetos, na Application (`Collection/` e `Discovery/`).

## 1. Agenda

`CollectionScheduler` (Worker), `CollectionCycle` (Application) e `CollectionSchedule` (Domain).

- **Fonte devida**: nenhuma execução concluída começou no horário marcado mais recente ou depois dele. Vários horários perdidos com a máquina desligada viram uma única coleta. O que conta como concluída está em [Fechamento](#5-fechamento-da-execução).
- **Ao iniciar**: execuções presas em `Running` viram `Interrupted`, módulos sem linha no banco são registrados e as fontes devidas são coletadas (`CatchUp`).
- **Checagem**: o relógio de parede é conferido a cada minuto. Timers longos param de contar com a máquina suspensa e disparariam atrasados.
- **Ciclo com falha**: um ciclo que falha inteiro (banco fora do ar, por exemplo) é tentado de novo no minuto seguinte.
- **Paralelismo**: as fontes devidas são coletadas em paralelo, até `MaxConcurrentSources`. Dentro de uma fonte, a coleta é sequencial, porque o rate limit vale por host.

## 2. Descoberta

`SourceHarvester` e `DiscoveryStopPolicy` (Application), com o discoverer do módulo da fonte.

O discoverer entrega os itens do mais novo para o mais antigo e busca as páginas da listagem sob demanda. Quando a regra de parada decide parar, nenhuma página a mais é pedida.

### Aceitação de um item listado

Um item vira candidato só se tiver URL http(s) válida, host entre os `AllowedHosts` da fonte e título não vazio. O resto conta como `Skipped`, sem requisição.

### Identidade do artigo

Um artigo é identificado pela URL canônica, normalizada assim:

1. Esquema e host em minúsculas; porta padrão removida.
2. Fragmento (`#...`) removido.
3. Parâmetros de rastreamento (`utm_*`, `fbclid`, `gclid`) removidos.
4. Demais parâmetros ordenados.

O SHA-256 dessa URL é a chave única de `Articles`.

### Deduplicação

| Onde | Pega |
| --- | --- |
| Conjunto da execução | O mesmo artigo listado por mais de um feed da fonte |
| Consulta em lote (25 por vez) antes de baixar a página | Artigos coletados em execuções anteriores |
| Índice único em `UrlHash`, na [gravação](#4-gravação) | Corridas entre execuções e canonical da página igual a um artigo já gravado |

### Regra de parada da descoberta

A `DiscoveryStopPolicy` avalia os itens na ordem em que o discoverer entrega. A cada item, checa nesta ordem:

1. **Teto**: atingiu `MaxItemsPerRun` → para (`ReachedItemCap`).
2. **Sequência de conhecidos**: os últimos `KnownStreakToStop` itens já estavam no banco → para (`ReachedKnownItems`). Uma sequência, e não um único item, tolera matérias antigas fixadas no topo.
   Só contam os conhecidos publicados até a marca d'água, ou sem data. Um conhecido mais novo que a marca d'água pode ter sido gravado por uma execução que não terminou, e parar nele deixaria para trás os artigos mais antigos que ela não alcançou.
3. **Limite de data**: item publicado antes do limite inferior → para (`ReachedLowerBound`). Itens sem data nunca param por idade.

O limite inferior é:
- com marca d'água: `marca d'água − WatermarkOverlap`;
- sem marca d'água (primeira coleta): `agora − InitialLookback`;
- em qualquer caso, nunca antes de `agora − RetentionWindow`.

Se a fonte acaba sem nenhuma dessas condições, o motivo é `EndOfSource`.

**Possível lacuna** (`PossibleGap`): a execução fica marcada quando parou no teto, ou quando a fonte acabou sem chegar a nada coletado por uma execução concluída. A segunda condição só vale com marca d'água e com pelo menos um item avaliado. Em ambos os casos pode haver artigos perdidos entre esta execução e a anterior.

## 3. Leitura do texto

`ArticleIngestor` e `ArticleContentReader` (Application), com o extrator do módulo da fonte. Vale para cada artigo novo.

1. A página é baixada. Se o robots.txt a bloqueia ou o download falha, o artigo é gravado como `LeadOnly`, com o título e o lead da listagem.
2. O extrator do módulo roda. Se o texto tiver pelo menos `MinExtractedChars`, vale.
3. Senão, roda o extrator genérico (`ReadabilityExtractor`); se ele trouxer texto suficiente, vale e conta como `ExtractionFallback`.
4. Se o genérico também falhar, vale o texto curto do módulo, se houver; senão o artigo fica `LeadOnly`.

Exceção lançada por um extrator conta como falha de extração daquele artigo e não interrompe a execução.

## 4. Gravação

`ArticleIngestor` (Application) e o repositório de artigos (Infrastructure).

- **URL gravada**: o canonical declarado pela página substitui a URL listada só quando é válido e fica num host da fonte. Um canonical apontando para outro domínio é ignorado.
- **Duplicata**: a violação do índice único em `UrlHash` vira `Duplicate`, não erro.
- **Texto**: gravado comprimido com Brotli pela aplicação.
- **Momento**: cada artigo é gravado assim que é lido, sem esperar o fim da execução. Por isso uma execução interrompida deixa gravados os artigos mais novos, e a [regra de parada](#regra-de-parada-da-descoberta) leva isso em conta.

## 5. Fechamento da execução

`CollectSource` (Application).

### Status e efeito na fonte

| Status | Quando | Efeito na `Source` |
| --- | --- | --- |
| `Succeeded` | Terminou sem falhas | Grava início da execução, marca d'água e estado |
| `PartiallySucceeded` | Terminou com falhas, mas descobriu itens | Igual a `Succeeded` |
| `Failed` | Falhas sem nenhum item descoberto, ou erro inesperado (`Abort`) | Grava só o início da execução |
| `Interrupted` | Desligamento durante a execução, ou execução presa em `Running` encontrada ao iniciar | Nada |

O início da última execução concluída (`LastCompletedRunStartedAt`) decide se a fonte está devida na [agenda](#1-agenda). Uma execução `Failed` conta como concluída e espera o próximo horário; uma `Interrupted` não conta, e a fonte é coletada de novo no próximo início.

### Marca d'água e estado da descoberta

- **Marca d'água**: é a data de publicação mais recente vista numa execução concluída. Datas no futuro são ignoradas, porque empurrariam a marca para frente e esconderiam artigos reais.
- **Estado da descoberta**: ETags, cursores e qualquer outro estado do adapter, que o núcleo guarda sem interpretar.
- **Quando os dois são gravados**: só em execução `Succeeded` ou `PartiallySucceeded`. Execução interrompida ou com falha total não os altera, então a próxima cobre a mesma janela e a deduplicação torna a repetição inofensiva.
- **Execução que bateu no teto**: mantém o estado anterior da descoberta, para que a próxima liste de novo os itens que ficaram para trás.

### Alerta de conteúdo degradado

A execução registra um `Warning` quando mais de `ContentAlertRate` dos artigos novos usaram o fallback ou ficaram `LeadOnly`, desde que haja pelo menos `ContentAlertMinArticles` artigos novos. O fallback conta porque, após uma mudança de layout, o extrator genérico ainda devolve texto e o artigo não vira `LeadOnly`.

## Pipeline HTTP

Infrastructure. Toda requisição da [descoberta](#2-descoberta) e da [leitura do texto](#3-leitura-do-texto) passa por ele.

Toda requisição a uma fonte passa pelo cliente nomeado `CollectorHttp.ClientName`, obtido com `IHttpClientFactory.CreateClient`. Nunca se usa `new HttpClient()`. Criar e descartar clientes esgota sockets, e um cliente estático não acompanha mudanças de DNS. A factory reaproveita as conexões e permite que os módulos de fonte usem o pipeline sem depender da Infrastructure: o nome é uma constante da Application.

### Configuração do cliente

| Item | Valor | Por quê |
| --- | --- | --- |
| `User-Agent` | `Collector:Http:UserAgent` (`produto/versão`) | Identifica o robô; o produto é o token procurado no robots.txt |
| Tamanho máximo da resposta | `Collector:Http:MaxResponseBytes` | Corta respostas anormais antes de ocupar memória |
| Descompressão automática | gzip, deflate e brotli | Algumas fontes (o G1, por exemplo) mandam gzip sem que seja pedido |
| `PooledConnectionLifetime` | 5 min | Renova conexões e acompanha mudanças de DNS |

### Handlers

Os `DelegatingHandler`s rodam na ordem em que são registrados ([DependencyInjection.cs](../../src/ArticleMonitor.Infrastructure/DependencyInjection.cs)):

```
requisição
  → RobotsTxtHandler      bloqueia caminhos proibidos, antes de qualquer acesso à rede
  → resiliência padrão    timeout total, retry, circuit breaker, timeout por tentativa
  → HostRateLimitHandler  espera a vez do host
  → SocketsHttpHandler    rede
```

A ordem tem consequências:
- O robots.txt é checado uma vez por requisição, não a cada retry.
- Cada retry também espera sua vez no host.
- A espera na fila do rate limiter conta dentro do timeout de 10 s de cada tentativa.

Esse último ponto não pesa porque cada fonte faz uma requisição por vez.

Existe um segundo cliente, `robots`, usado só para baixar o robots.txt. Ele tem resiliência e rate limit, mas não o `RobotsTxtHandler`, que entraria em recursão.

### robots.txt

- **Cache**: a política de cada host fica guardada por `Collector:Http:RobotsTxtCacheDuration` (24 h por padrão).
- **Resposta 4xx**: não há regras, então tudo é permitido.
- **Resposta 5xx ou erro de rede**: tudo é bloqueado, como manda a RFC 9309. A nova tentativa vem em 10 min, para que uma falha passageira não bloqueie o host por um dia.
- **Grupo aplicado**: o do token do User-Agent; se não houver, o grupo `*`.
- **Desempate**: vence a regra mais longa; empate entre Allow e Disallow, vence Allow. `*` e `$` são suportados.
- **Segurança**: o arquivo é entrada remota, então as regras viram regex non-backtracking com timeout.
- **`Crawl-delay`**: não é lido; o rate limiter já espaça as requisições.

### Resiliência padrão

`AddStandardResilienceHandler()` (Polly v8) aplica, de fora para dentro:

| Estratégia | Padrão | Comportamento |
| --- | --- | --- |
| Limite de concorrência | 1.000 simultâneas | Proteção contra excesso |
| Timeout total | 30 s | Teto da requisição inteira, retries incluídos |
| Retry | 3 tentativas, backoff exponencial com jitter a partir de 2 s | Repete em 5xx, 408, 429, erro de rede e timeout de tentativa; respeita `Retry-After` |
| Circuit breaker | Abre com ≥ 10% de falhas em 30 s (mínimo de 100 requisições) e fica aberto 5 s | Para de insistir num destino que está caindo |
| Timeout por tentativa | 10 s | Limite de cada tentativa |

O pipeline é um por cliente, não por host: falhas de um site contam no circuit breaker de todos os sites do cliente `collector`. Com 1 requisição por segundo por host, o mínimo de 100 requisições em 30 s não é atingido com poucas fontes. Com muitas fontes, `.SelectPipelineByAuthority()` separa o pipeline por host.

### Rate limit por host

`HostRateLimiter` é um token bucket particionado por host:
- **Ficha**: cada host tem uma, reposta a cada `1 / Collector:Http:RequestsPerSecondPerHost` segundos.
- **Fila**: requisições sem ficha esperam em ordem de chegada.
- **Partição**: hosts diferentes não esperam um pelo outro.
- **Instância**: é singleton, compartilhado por todos os módulos e pelos dois clientes. Assim nenhum caminho do código ultrapassa o limite de um host.

### Como as falhas chegam ao núcleo

| Falha | Vira |
| --- | --- |
| Caminho proibido pelo robots.txt (`RobotsDisallowedException`) | Página `BlockedByRobots` (artigo `LeadOnly`) ou feed com falha reportada |
| Erro de rede, timeout, circuito aberto, resposta grande demais, status não 2xx | Falha da página ou do feed; a execução continua |
| Cancelamento por desligamento | Propaga, e a execução vira `Interrupted` |

Nos testes, o `StubHttpClientFactory` (em `ArticleMonitor.Sources.Tests`) devolve um `HttpClient` com respostas falsas no lugar de todo esse pipeline.
