# Avaliação: stack .NET (C#, ASP.NET Core, SQL Server)

Viabilidade de engenharia e custo do article-monitor numa stack .NET, a partir das premissas de [project.md](project.md). Preços e limites consultados em 2026-11-08; links em [Fontes](#fontes).

## Conclusão

- **Viável.** Todos os componentes têm solução madura no ecossistema .NET. Os pontos fracos são a extração de conteúdo (não há equivalente ao trafilatura) e a hospedagem: o SQL Server só roda em x64 e exige 2 GB de RAM, o que exclui as VMs gratuitas mais generosas.
- **Custo mínimo:** US$ 0 rodando tudo numa máquina x64 própria ligada 24 h, ou ~€ 6/mês numa VPS x64 de 4 GB. Um arranjo 100% gratuito na nuvem existe, mas é frágil (ver [Cenários de custo](#cenários-de-custo)).
- **LLM:** na nuvem, o custo é de centavos de dólar por mês com o Claude Haiku 5.5. Rodar local se justifica por não depender de conta e chave de API, não por economia. O risco do local é a qualidade da nuvem de termos com modelos pequenos.

## Stack proposta

| Camada | Escolha | Por quê |
| --- | --- | --- |
| Runtime | .NET 10 (LTS, suporte até 14/11/2028) | O .NET 11 sai em 10/11/2026; as bibliotecas acompanham o LTS primeiro e migrar depois é simples |
| API | ASP.NET Core Minimal APIs, com OpenAPI e validação nativos | Poucos endpoints; dispensa controllers e bibliotecas de validação |
| Autenticação da API | Chave compartilhada num header, via `AuthenticationHandler` | O único cliente é o bot |
| Worker | Worker Service (`BackgroundService` + `PeriodicTimer`) | O agendamento por fonte vem de uma tabela (próxima execução por fonte); Quartz ou Hangfire seriam excesso |
| Banco | SQL Server 2025 Express (local) ou Azure SQL Database free tier | O Express é gratuito para produção, até 50 GB; os dois têm o tipo `VECTOR` nativo |
| Acesso a dados | EF Core 10 | Migrations e suporte nativo a `VECTOR` (`SqlVector<float>`, `EF.Functions.VectorDistance`) |
| HTTP da coleta | `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience` + `System.Threading.RateLimiting` | Retry, timeout e rate limit por domínio sem código próprio |
| RSS/Atom | `System.ServiceModel.Syndication` | Pacote oficial; cobre RSS 2.0 e Atom 1.0 |
| Extração | SmartReader (port do Readability) + AngleSharp nos adapters específicos | Melhor opção em .NET; qualidade a validar nas fontes reais |
| Normalização e stemming | `Lucene.Net.Analysis.Common` (analisadores de português e inglês) | Stemmers prontos para os dois idiomas (ver [Matching](#matching-sem-full-text-search)) |
| LLM | `Microsoft.Extensions.AI` (`IChatClient`) + OllamaSharp (local) ou SDK oficial da Anthropic (nuvem) | Os dois implementam `IChatClient`: trocar de provedor é configuração |
| Embeddings | `IEmbeddingGenerator` + OllamaSharp com `embeddinggemma` | Modelo multilíngue (100+ idiomas) que roda em CPU |
| Discord | Discord.Net 3.20 | Versão estável; o NetCord é mais moderno, mas ainda está em beta |
| Testes | xUnit v3, `Testcontainers.MsSql`, `WebApplicationFactory` | Testes de integração contra SQL Server real em container |
| Empacotamento | `dotnet publish -t:PublishContainer` + Docker Compose | Gera imagens sem Dockerfile; um `compose.yaml` sobe banco, Ollama e os três processos |

### Estrutura da solução

```
src/
  ArticleMonitor.Core            normalização, score, heat, retenção e interfaces (portas)
  ArticleMonitor.Infrastructure  EF Core, feeds, extração, clientes de LLM e embeddings
  ArticleMonitor.Contracts       DTOs da API, compartilhados com o bot
  ArticleMonitor.Api
  ArticleMonitor.Worker
  ArticleMonitor.Bot             referencia só Contracts e fala com a API por HTTP
tests/
  ArticleMonitor.Core.Tests
  ArticleMonitor.Integration.Tests
```

- O Core não depende de banco, HTTP nem LLM, então o score é testado com textos reais sem infraestrutura.
- O bot não referencia Core nem Infrastructure: o compilador garante que a interface fica separada da lógica.
- Segredos (token do Discord, chave da API, connection string) ficam em user-secrets no desenvolvimento e em variáveis de ambiente na execução, nunca no repositório.

### Paralelismo na coleta

- **Fontes:** `Parallel.ForEachAsync` sobre as fontes, com `MaxDegreeOfParallelism` configurável e timeout por fonte. Uma fonte com falha é registrada e não interrompe as demais.
- **Embeddings:** são gerados em lotes por ciclo, não em paralelo. O cálculo roda na CPU, via Ollama, e chamadas simultâneas não aceleram nada.
- **Várias instâncias do worker, se um dia forem necessárias:** cada instância reserva fontes vencidas com um `UPDATE` atômico (`UPDLOCK, READPAST`) que grava a validade da reserva. Se a instância cair, a reserva expira. Dispensa fila.

## Matching sem full-text search

A recomendação é calcular o score lexical na aplicação, sem o full-text search do SQL Server:

1. O score já exige código próprio (posição, termos distintos, pesos, negativos). O full-text só resolveria acentos e stemming, que o Lucene.NET resolve em memória.
2. O índice full-text do SQL Server é populado de forma assíncrona. Um artigo recém-inserido não aparece na busca na hora, e o worker teria de esperar a população para pontuar.
3. No Linux e no Docker, o full-text exige um pacote à parte (`mssql-server-fts`), ou seja, uma imagem customizada.
4. Sem índice full-text, o texto pode ser guardado comprimido pela aplicação (Brotli), o que reduz o banco e poupa CPU do servidor.
5. A regra fica coberta por testes de unidade e não amarrada ao banco.

O custo é o match retroativo: ao cadastrar uma palavra, o worker relê em lotes os ~60 mil artigos da janela (~100 MB comprimidos). A estimativa é de poucos minutos por palavra, a validar.

- **Idioma por fonte:** o idioma é configurado no cadastro da fonte (G1 em português, NYT em inglês) e escolhe o analisador. Não há detecção automática.
- **Embedding de título e lead:** o embedding usa título e lead (ou o abstract, no NYT), não o texto inteiro. Assim, fontes com conteúdo parcial ficam comparáveis às demais.
- **Similaridade retroativa:** com embedding de todos os artigos, a similaridade com uma palavra nova é uma única consulta com `VECTOR_DISTANCE` sobre a janela. Com 60 mil linhas a busca exata basta; o índice vetorial (DiskANN) ainda é preview e não é necessário.

## Armazenamento

Estimativa por artigo nesta stack, a confirmar com dados reais:

| Dado | Tamanho aprox. |
| --- | --- |
| Metadados | ~0,5 KB |
| Texto comprimido pela aplicação | ~1,5–2 KB |
| Embedding em float32 | ~3 KB com 768 dimensões; ~1 KB com 256 |
| **Total** | **~3–5,5 KB** |

- **Janela de 60 dias:** ~60 mil artigos ocupam ~180–330 MB, abaixo da meta de 1 GB e longe dos limites do Express (50 GB) e do Azure free (32 GB).
- **Dimensão do embedding:** o `embeddinggemma` permite truncar o vetor para 512, 256 ou 128 dimensões com pouca perda (Matryoshka). 256 é um bom ponto de partida.
- **Heat:** só é gravado para os pares artigo × palavra com score acima de zero. Gravar todos os pares multiplicaria linhas sem uso.
- **Espaço liberado:** zerar texto e embedding com `UPDATE` devolve as páginas à própria tabela. Um `ALTER INDEX ... REORGANIZE` periódico compacta o espaço de LOB.
- **Limpeza diária:** o Express não tem SQL Server Agent, então a limpeza roda no worker, como já previsto.

## LLM e embeddings locais

| Tarefa | Volume | Local (Ollama, CPU) | Nuvem (Claude Haiku 5.5) |
| --- | --- | --- | --- |
| Gerar a nuvem de termos | Uma por palavra cadastrada (~500 tokens de entrada, ~600 de saída) | Modelo de 4–8B parâmetros: ~30–90 s por nuvem (aprox.) | ~US$ 0,0004 por palavra, mais o thinking |
| Classificar limítrofes (opcional) | ~50 artigos/dia (estimativa) | Viável, mas disputa CPU com a coleta | < US$ 0,50/mês |
| Embeddings | ~1.000 artigos/dia | `embeddinggemma` (~300M parâmetros) em CPU, com folga | Ficam locais nos dois casos |

- **Preço do Haiku 5.5:** US$ 0,10 por milhão de tokens de entrada e US$ 0,50 por milhão de saída. O thinking é cobrado como saída; com esforço baixo, o total continua em centavos.
- **Hardware:** um modelo de 4B quantizado ocupa ~3 GB de RAM e um de 8B, ~5–6 GB. Eles só ficam carregados durante o uso: o Ollama descarrega o modelo após 5 minutos ocioso. GPU é opcional, porque a nuvem é gerada raramente.
- **Saída estruturada:** Ollama e Anthropic aceitam um JSON schema para a resposta, o que reduz respostas malformadas. Validar a resposta contra o schema continua obrigatório.
- **Qualidade:** modelos pequenos tendem a gerar nuvens piores em português, com pesos incoerentes e termos genéricos. Antes de decidir, vale gerar a nuvem de 5 a 10 palavras reais com os dois e comparar. Como a nuvem é editável pelo Discord, um resultado mediano pode ser corrigido.
- **Quem chama o LLM:** a API não espera o LLM. Ela grava a palavra como pendente, e o worker gera a nuvem e roda o match retroativo. Um LLM local de 30–90 s estouraria o timeout de uma requisição síncrona.
- **Prazo do Discord:** o slash command precisa ser reconhecido em 3 s. Por isso, `/palavra add` responde de forma adiada (defer) e consulta o status da palavra na API antes de dar o retorno.

## Cenários de custo

| Cenário | Banco | Processos e Ollama | Custo mensal | Limitações |
| --- | --- | --- | --- | --- |
| A. Tudo local | SQL Server 2025 Express em Docker | Mesma máquina x64; 16 GB de RAM recomendados com LLM local | US$ 0 + energia (mini PC de 10–15 W: ~7–11 kWh/mês) | Máquina ligada 24 h. Desligada, o bot sai do ar e os feeds de alto volume perdem itens |
| B. VPS x64 barata | SQL Server Express na VPS | Mesma VPS (ex.: Hetzner CX23, 2 vCPU, 4 GB); LLM pela API | ~€ 6 + centavos de LLM | 4 GB não comportam SQL Server e LLM local juntos; o plano costuma aparecer esgotado |
| C. Gratuito na nuvem | Azure SQL Database free tier | Oracle Always Free Ampere A1 (ARM, 2 OCPU, 12 GB), com LLM local | US$ 0 | A cota de compute do banco limita a coleta a poucas execuções por dia; a Oracle pode reclamar VM ociosa; dois provedores; banco sem SLA |

Hospedar os processos no próprio Azure (App Service, Container Apps) sai mais caro: processos ligados 24 h, como o bot no Gateway, estouram as cotas gratuitas.

**Uso de memória no cenário A (aprox.):**

| Componente | RAM |
| --- | --- |
| SQL Server Express (buffer pool limitado a 1.410 MB) | ~2–2,5 GB |
| API, worker e bot | ~0,3–0,5 GB |
| Ollama com `embeddinggemma` | ~0,5–1 GB |
| Ollama com LLM, só durante a geração | ~3–6 GB |
| **Total** | **~4 GB sem o LLM e ~7–9 GB com ele carregado** |

### Azure SQL free tier na prática

- **Cota:** 100.000 vCore-segundos e 32 GB de dados por mês. O banco pausa sozinho após 15 minutos sem atividade, e esse tempo é fixo.
- **Custo por ativação:** cada ativação consome o tempo de trabalho mais os 15 minutos até a pausa. Assumindo o mínimo de 0,5 vCore, são ~600 vCore-segundos por ativação de 5 minutos, e o dobro se a memória usada for alta.
- **Resultado:** cabem ~3 a 5 ativações por dia, somando coletas e consultas do bot. Doze coletas por dia esgotam a cota na metade do mês, e o banco fica pausado até o mês seguinte.
- **Conexões abertas:** qualquer conexão aberta impede a pausa. Nada de health check consultando o banco nem ferramenta de consulta esquecida conectada.
- **Retomada:** a primeira conexão após a pausa pode falhar enquanto o banco retoma, então o EF Core precisa de retry (`EnableRetryOnFailure`).
- **Feeds de alto volume:** com poucas coletas por dia, esses feeds podem descartar itens antes da próxima leitura. Feeds por editoria, que são menores, reduzem o risco.

### Por que o SQL Server pesa no custo

O SQL Server só roda em x64 e exige 2 GB de RAM. As VMs gratuitas da Oracle são ARM (A1) ou têm 1 GB (AMD Micro), então o banco precisa ir para o Azure ou para uma máquina própria. Com PostgreSQL, a VM ARM gratuita da Oracle hospedaria tudo, banco incluído. Fica registrado como custo dessa escolha.

## Riscos

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Extração do SmartReader pior que a do trafilatura | Texto incompleto ou com lixo (menus, "leia também") piora o score | Testar nas fontes reais antes de fechar a stack; adapters específicos com AngleSharp onde falhar |
| SQL Server só em x64 | Exclui hospedagem ARM gratuita | Cenários A ou B, ou o banco no Azure (C) |
| Cota do Azure SQL free | O banco pausa até o mês seguinte se a cota estourar | Poucas coletas por dia, alerta na métrica "Free amount remaining", nenhuma conexão ociosa |
| Qualidade do LLM local em português | Nuvens piores | Comparar com o Haiku 5.5; nuvem editável |
| Lucene.NET 4.8 ainda com rótulo beta | Atualizações lentas | Uso restrito aos analisadores, isolados atrás de uma interface |
| Oracle reclamar a VM ociosa | Bot e worker fora do ar | Cenário C só como experimento, com imagem de boot salva |

## Ordem de implementação sugerida

Cada etapa entrega algo testável de ponta a ponta. A etapa 1 valida o maior risco técnico, a extração.

1. Fontes, coleta RSS, extração, deduplicação e registro das execuções.
2. Cadastro de palavras, nuvem via LLM e score lexical.
3. API e bot com `/palavra` e `/buscar`.
4. Embeddings e composição do heat.
5. Job de retenção.
6. Feedback 👍/👎.

## O que muda em relação às premissas

| Premissa (pensada para PostgreSQL) | Nesta stack |
| --- | --- |
| Full-text com `unaccent` + `portuguese` | Normalização e stemming na aplicação, com Lucene.NET |
| `tsvector` armazenado | Não existe; o texto fica comprimido pela aplicação |
| pgvector | Tipo `VECTOR` nativo do SQL Server 2025 e do Azure SQL |
| Autovacuum reaproveita o espaço | A tabela reaproveita as páginas; `REORGANIZE` periódico |
| A API pede a nuvem ao LLM | O worker gera a nuvem; a API só registra a palavra como pendente |
| ~8–10 KB por artigo | ~3–5,5 KB por artigo |

## Fontes

- [Editions and supported features of SQL Server 2025](https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2025)
- [SQL Server on Linux: installation guidance and system requirements](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup)
- [Install SQL Server Full-Text Search on Linux](https://learn.microsoft.com/sql/linux/sql-server-linux-setup-full-text-search)
- [Azure SQL Database free tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer)
- [Azure SQL Database free tier FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier-faq)
- [EF Core: vector search in the SQL Server provider](https://learn.microsoft.com/ef/core/providers/sql-server/vector-search)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Visual Studio Magazine: .NET 11 RC1 ahead of the November launch](https://visualstudiomagazine.com/articles/2026/09/14/net-11-rc1-gets-go-live-support-ahead-of-november-launch.aspx)
- [Oracle Cloud Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/resourceref.htm)
- [Hetzner Cloud](https://www.hetzner.com/cloud/) e [especificações do CX23](https://www.vpsbenchmarks.com/hosters/hetzner/plans/cx23)
- [Anthropic: pricing](https://platform.claude.com/docs/en/about-claude/pricing)
- [Ollama: embed API](https://docs.ollama.com/api/embed)
- [Ollama: embeddinggemma](https://ollama.com/library/embeddinggemma)
- [Google: EmbeddingGemma](https://ai.google.dev/gemma/docs/embeddinggemma)
- [Discord.Net](https://www.nuget.org/packages/Discord.Net), [NetCord](https://www.nuget.org/packages/NetCord) e [SmartReader](https://www.nuget.org/packages/SmartReader) no NuGet
- [Lucene.NET: BrazilianAnalyzer](https://lucenenet.apache.org/docs/4.8.0-beta00017/api/analysis-common/Lucene.Net.Analysis.Br.BrazilianAnalyzer.html)
