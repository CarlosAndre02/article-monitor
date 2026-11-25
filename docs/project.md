# Project

## Camada Domain

`ArticleMonitor.Domain` é a camada mais interna e não referencia nenhum outro projeto nem pacote: só a biblioteca padrão do .NET, sem EF Core, HTTP, injeção de dependência ou logging. Por isso suas regras são testadas sem infraestrutura.

Ela tem dois tipos de objeto:

- **Entidades** (`Source`, `Article`, `CollectionRun`): têm identidade e ciclo de vida. As propriedades têm `private set` e só mudam por métodos que preservam as invariantes. Uma execução fechada não fecha de novo; a marca d'água nunca anda para trás.
- **Value objects** (`SourceKey`, `CanonicalUrl`, `UrlHash`, `ArticleHeadline`): são imutáveis, comparados por valor e validados na criação. Não existe instância inválida.

| Objeto | Invariante |
| --- | --- |
| `SourceKey` | Minúsculas, dígitos e hífens simples, até 50 caracteres |
| `CanonicalUrl` | URL absoluta http(s), normalizada (ver [Identidade do artigo](#identidade-do-artigo)), até 2.048 caracteres |
| `UrlHash` | SHA-256 da URL canônica (32 bytes), igualdade por conteúdo |
| `ArticleHeadline` | Título obrigatório; espaços colapsados; título truncado em 500 caracteres e lead em 2.000 |
| `Article` | Criado só por `Article.Collect`; sem texto, fica `LeadOnly` |
| `Source` | A marca d'água só avança |
| `CollectionRun` | Contadores só mudam por `Record*`; fecha uma única vez (`Finish`, `Abort` ou `Interrupt`) |
| `CollectionSchedule` | Pelo menos um horário; horários interpretados no fuso configurado; horários inexistentes por horário de verão são pulados |

As duas formas de criação têm papéis diferentes:

- **`TryCreate`** devolve `false` para entrada inválida vinda de fora (feed, página), que é uma situação esperada.
- **`From`, `Start` e os construtores** lançam exceção, porque receber valor inválido ali é bug de quem chama.

Os construtores privados sem parâmetros marcados "Used by EF Core" existem só para o EF materializar as entidades. O mapeamento para tabelas fica na Infrastructure (`Persistence/Configurations`), e o Domain não sabe que é persistido.

## Regras de negócio

As invariantes de um objeto ficam no Domain. As regras que coordenam vários objetos (regra de parada, deduplicação, fallback de extração) ficam na Application, em `Collection/` e `Discovery/`.

### Identidade do artigo

Um artigo é identificado pela URL canônica, normalizada assim:

1. Esquema e host em minúsculas; porta padrão removida.
2. Fragmento (`#...`) removido.
3. Parâmetros de rastreamento (`utm_*`, `fbclid`, `gclid`) removidos.
4. Demais parâmetros ordenados.

O SHA-256 dessa URL é a chave única de `Articles`. O canonical declarado pela página substitui a URL listada só quando é válido e fica num host da fonte; um canonical apontando para outro domínio é ignorado.

### Aceitação de um item listado

Um item vira candidato só se tiver URL http(s) válida, host entre os `AllowedHosts` da fonte e título não vazio. O resto conta como `Skipped`, sem requisição.

### Deduplicação

| Camada | Pega |
| --- | --- |
| Conjunto da execução | O mesmo artigo listado por mais de um feed da fonte |
| Consulta em lote (25 por vez) antes de baixar a página | Artigos coletados em execuções anteriores |
| Índice único em `UrlHash` | Corridas entre execuções e canonical da página igual a um artigo já gravado; a violação vira `Duplicate`, não erro |

### Regra de parada da descoberta

A `DiscoveryStopPolicy` avalia os itens na ordem em que o discoverer entrega, do mais novo para o mais antigo. A cada item, checa nesta ordem:

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

### Marca d'água e estado da descoberta

- **Marca d'água**: é a data de publicação mais recente vista numa execução concluída. Datas no futuro são ignoradas, porque empurrariam a marca para frente e esconderiam artigos reais.
- **Estado da descoberta**: ETags, cursores e qualquer outro estado do adapter, que o núcleo guarda sem interpretar.
- **Quando os dois são gravados**: só em execução `Succeeded` ou `PartiallySucceeded`. Execução interrompida ou com falha total não os altera, então a próxima cobre a mesma janela e a deduplicação torna a repetição inofensiva.
- **Execução que bateu no teto**: mantém o estado anterior da descoberta, para que a próxima liste de novo os itens que ficaram para trás.

### Conteúdo do artigo

1. A página é baixada. Se o robots.txt a bloqueia ou o download falha, o artigo é gravado como `LeadOnly`, com o título e o lead da listagem.
2. O extrator do módulo roda. Se o texto tiver pelo menos `MinExtractedChars`, vale.
3. Senão, roda o extrator genérico (`ReadabilityExtractor`); se ele trouxer texto suficiente, vale e conta como `ExtractionFallback`.
4. Se o genérico também falhar, vale o texto curto do módulo, se houver; senão o artigo fica `LeadOnly`.

Exceção lançada por um extrator conta como falha de extração daquele artigo e não interrompe a execução.

**Alerta de conteúdo degradado**: a execução registra um `Warning` quando mais de `ContentAlertRate` dos artigos novos usaram o fallback ou ficaram `LeadOnly`, desde que haja pelo menos `ContentAlertMinArticles` artigos novos. O fallback conta porque, após uma mudança de layout, o extrator genérico ainda devolve texto e o artigo não vira `LeadOnly`.

### Status da execução e efeito na fonte

| Status | Quando | Efeito na `Source` |
| --- | --- | --- |
| `Succeeded` | Terminou sem falhas | Grava início da execução, marca d'água e estado |
| `PartiallySucceeded` | Terminou com falhas, mas descobriu itens | Igual a `Succeeded` |
| `Failed` | Falhas sem nenhum item descoberto, ou erro inesperado (`Abort`) | Grava só o início da execução |
| `Interrupted` | Desligamento durante a execução, ou execução presa em `Running` encontrada ao iniciar | Nada |

O início da última execução concluída (`LastCompletedRunStartedAt`) decide se a fonte está devida na agenda. Uma execução `Failed` conta como concluída e espera o próximo horário; uma `Interrupted` não conta, e a fonte é coletada de novo no próximo início.

### Agenda

- **Fonte devida**: nenhuma execução concluída começou no horário marcado mais recente ou depois dele. Vários horários perdidos com a máquina desligada viram uma única coleta.
- **Ao iniciar**: execuções presas em `Running` viram `Interrupted`, módulos sem linha no banco são registrados e as fontes devidas são coletadas (`CatchUp`).
- **Checagem**: o relógio de parede é conferido a cada minuto. Timers longos param de contar com a máquina suspensa e disparariam atrasados.
- **Ciclo com falha**: um ciclo que falha inteiro (banco fora do ar, por exemplo) é tentado de novo no minuto seguinte.

## Cliente HTTP e pipeline de resiliência

Toda requisição a uma fonte passa pelo cliente nomeado `CollectorHttp.ClientName`, obtido com `IHttpClientFactory.CreateClient`. Nunca se usa `new HttpClient()`. Criar e descartar clientes esgota sockets, e um cliente estático não acompanha mudanças de DNS. A factory reaproveita as conexões e permite que os módulos de fonte usem o pipeline sem depender da Infrastructure: o nome é uma constante da Application.

### Configuração do cliente

| Item | Valor | Por quê |
| --- | --- | --- |
| `User-Agent` | `Collector:Http:UserAgent` (`produto/versão`) | Identifica o robô; o produto é o token procurado no robots.txt |
| Tamanho máximo da resposta | `Collector:Http:MaxResponseBytes` | Corta respostas anormais antes de ocupar memória |
| Descompressão automática | gzip, deflate e brotli | Algumas fontes (o G1, por exemplo) mandam gzip sem que seja pedido |
| `PooledConnectionLifetime` | 5 min | Renova conexões e acompanha mudanças de DNS |

### Pipeline

Os `DelegatingHandler`s rodam na ordem em que são registrados ([DependencyInjection.cs](../src/ArticleMonitor.Infrastructure/DependencyInjection.cs)):

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
