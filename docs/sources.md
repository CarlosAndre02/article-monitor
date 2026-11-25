# Fontes de notícias

Cada fonte é um módulo na pasta `src/ArticleMonitor.Sources/<Nome>` (namespace `ArticleMonitor.Sources.<Nome>`) que implementa `ISourceModule`. O módulo responde só a duas perguntas: como listar os artigos da fonte e como ler o texto de um artigo.

Todo o resto é do núcleo e vale igual para todas as fontes:
- agenda e recuperação ao iniciar;
- deduplicação e regra de parada;
- fallback de extração;
- persistência e registro das execuções;
- robots.txt, rate limit por host, resiliência e limite de tamanho das respostas.

## O contrato

| Peça | Responsabilidade | Regras |
| --- | --- | --- |
| `SourceDefinition` | Chave, nome, idioma e hosts permitidos | A chave identifica a fonte no banco e não deve mudar. Links fora dos hosts são descartados. |
| `IArticleDiscoverer` | Listar os artigos | Do mais novo para o mais antigo, considerando todas as listagens da fonte. Páginas buscadas sob demanda: quando o núcleo para de enumerar, nenhuma requisição a mais é feita. Falhas vão para `DiscoveryContext.ReportFailure`, sem exceção. Estado próprio (ETags, cursores) vai para `DiscoveryContext.SaveState`. Requisições só pelo `HttpClient` `CollectorHttp.ClientName`. |
| `IArticleContentExtractor` | Ler o texto da página | Função pura. Layout desconhecido devolve `ExtractionResult.Failed`, sem exceção. Parágrafos separados por linha em branco. O canonical da página é opcional. |

O núcleo só persiste o estado da descoberta quando a execução termina. Uma execução interrompida nunca faz a fonte pular artigos.

Um módulo usa só o próprio código e os blocos de `Common/`, nunca outro módulo. Como todas as fontes ficam no mesmo projeto, para o número de projetos não crescer com elas, quem garante isso é o `SourceIsolationTests`, e não o compilador.

Blocos prontos em `Common/` (namespace `ArticleMonitor.Sources.Common`):
- `RssDiscoverer`: RSS/Atom com GET condicional (ETag/If-Modified-Since). Um feed com falha não interrompe os outros.
- `HtmlText`: normalização de texto e leitura do canonical.
- `ReadabilityExtractor`: o fallback genérico. O núcleo o aplica sozinho quando o extrator do módulo não traz texto suficiente; o módulo não precisa chamá-lo.

## Passo a passo (exemplo: CNN Brasil)

1. Criar a pasta `src/ArticleMonitor.Sources/CnnBrasil`, com namespace `ArticleMonitor.Sources.CnnBrasil`.
2. Implementar `CnnBrasilSourceModule : ISourceModule`. Se a fonte tiver RSS, usar o `RssDiscoverer`; senão, escrever um discoverer paginado que siga o contrato. Escrever um extrator específico só se o genérico não bastar.
3. Criar a extensão `AddCnnBrasilSource()`, com `services.AddSingleton<ISourceModule, CnnBrasilSourceModule>()`, e chamá-la no `Program.cs` do Worker.
4. Criar a pasta `tests/ArticleMonitor.Sources.Tests/CnnBrasil`, com as fixtures em `CnnBrasil/Fixtures`, classes que herdam `ArticleDiscovererContractTests` e `ArticleContentExtractorContractTests`, e os testes específicos da fonte.
5. Rodar `dotnet test`.

Ao iniciar, o Worker cria a linha da fonte nova no banco. Não precisa de migration.

## Fixtures

As fixtures são sintéticas: reproduzem a estrutura real do site (HTML, XML) com texto próprio. O texto das matérias tem direitos autorais e não deve ser versionado.

Antes de escrever as fixtures, valide o discoverer e o extrator contra feeds e páginas reais, fora do repositório.

## Desativar uma fonte

- **Pausa**: `IsEnabled = 0` na tabela `Sources`. A fonte para de ser coletada, mas o histórico fica.
- **Remoção do código**: tirar o registro do módulo no Worker. A linha no banco continua, e o Worker a ignora com um aviso no log.
