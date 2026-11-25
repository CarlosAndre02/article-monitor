# Project

O modelo de domínio, compartilhado por todas as features. As regras de negócio de cada feature estão em [features/](features/).

## Camada Domain

`ArticleMonitor.Domain` é a camada mais interna e não referencia nenhum outro projeto nem pacote: só a biblioteca padrão do .NET, sem EF Core, HTTP, injeção de dependência ou logging. Por isso suas regras são testadas sem infraestrutura.

Ela tem dois tipos de objeto:

- **Entidades** (`Source`, `Article`, `CollectionRun`): têm identidade e ciclo de vida. As propriedades têm `private set` e só mudam por métodos que preservam as invariantes. Uma execução fechada não fecha de novo; a marca d'água nunca anda para trás.
- **Value objects** (`SourceKey`, `CanonicalUrl`, `UrlHash`, `ArticleHeadline`): são imutáveis, comparados por valor e validados na criação. Não existe instância inválida.

| Objeto | Invariante |
| --- | --- |
| `SourceKey` | Minúsculas, dígitos e hífens simples, até 50 caracteres |
| `CanonicalUrl` | URL absoluta http(s), normalizada (ver [Identidade do artigo](features/coleta.md#identidade-do-artigo)), até 2.048 caracteres |
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
