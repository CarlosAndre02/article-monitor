# Testes

Um teste existe para reduzir um risco específico. Antes de escrever um, vale perguntar: **que erro ele pegaria, e qual é o teste mais barato capaz de pegá-lo?**

Os comandos para rodar as suítes estão em [How to test](../README.md#how-to-test).

## Como escolher

Quanto mais do sistema real um teste exercita, mais confiança ele dá, e mais lento, caro e instável ele fica. Por isso a distribuição costuma ter a forma de uma pirâmide:

```
        /\        E2E: poucos, cobrem os fluxos críticos
       /  \
      /----\      Integração: as fronteiras com banco, rede e arquivos
     /      \
    /--------\    Unitários: muitos, cobrem regras e casos de borda
```

Cada regra deve ser testada no nível mais baixo que consiga provar que ela funciona. Os casos de borda da regra de parada cabem em testes unitários de milissegundos. Já a garantia de que o banco rejeita um artigo duplicado só pode ser provada com o banco real.

## Os principais tipos

### Unitário

Verifica **uma unidade de comportamento isolada** do mundo externo: sem banco, rede, disco ou relógio real. A unidade não precisa ser uma única classe. O que importa é o teste ser rápido, determinístico e apontar exatamente o que quebrou.

```csharp
[Fact]
public void Never_goes_back_further_than_the_retention_window()
{
    var source = SourceWithWatermark(Now.AddDays(-100));
    var options = new CollectionOptions { RetentionWindow = TimeSpan.FromDays(60) };

    var policy = DiscoveryStopPolicy.For(source, options, Now);

    Assert.Equal(Now.AddDays(-60), policy.LowerBound);
}
```

O formato é **Arrange, Act, Assert**: prepara o cenário, executa uma ação e confere o resultado. Dependências externas são trocadas por **fakes**, implementações simples dos contratos, como um repositório em memória. É a Clean Architecture que torna isso possível: a Application só conhece interfaces.

- **Use para:** regras de negócio, cálculos, parsers, validações e casos de borda.
- **Não prova:** que as peças funcionam juntas, nem que o fake se comporta como a dependência real.
- **Cuidado:** teste o resultado observável, não os detalhes internos. Um teste que quebra em toda refatoração, mesmo com o comportamento intacto, atrapalha mais do que protege.

### Integração

Verifica se o seu código **funciona com uma dependência real**: banco, fila, sistema de arquivos. Aqui o SQL Server é real, iniciado em um container pelo Testcontainers.

```csharp
[Fact]
public async Task Concurrent_inserts_of_the_same_article_store_it_once()
{
    await using var app = await TestApplication.StartAsync(sql, Ct);
    var sourceId = await RegisterSourceAsync(app);

    var results = await Task.WhenAll(
        app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a"), Ct)),
        app.WithDbAsync(db => new ArticleRepository(db).TryAddAsync(NewArticle(sourceId, "a"), Ct)));

    Assert.Equal([AddArticleResult.Added, AddArticleResult.Duplicate], results.Order());
    Assert.Equal(1, await app.WithDbAsync(db => db.Articles.CountAsync(Ct)));
}
```

Um fake não conseguiria provar esse caso, porque a garantia depende do índice único e do erro de chave duplicada que só o SQL Server produz.

- **Use para:** queries, migrations, transações, concorrência e configuração do ORM.
- **Não prova:** que o sistema inteiro funciona contra os serviços externos reais.
- **Cuidado:** cada teste precisa do próprio estado. Testes que compartilham dados passam ou falham conforme a ordem de execução.

### End-to-end (E2E)

Exercita **um fluxo completo, como em produção**, de fora para dentro e com o mínimo de simulação. Neste projeto, é o worker real baixando o G1 real, extraindo o texto e gravando no SQL Server.

A diferença para a integração está no que é real. O teste `A_catch_up_cycle_stores_articles_and_records_the_run` percorre quase todo o ciclo, mas a fonte e as páginas são falsas, então ele continua sendo de integração. Ele nunca perceberia que o G1 mudou o HTML. Um E2E perceberia.

- **Use para:** poucos fluxos críticos, aqueles cuja falha faz o produto deixar de cumprir seu propósito.
- **Não serve para:** cobrir casos de borda. Quando falha, a causa pode estar em qualquer camada.
- **Cuidado:** E2E tende a ser **flaky** (falha sem que o código tenha mudado) por causa de rede, dados e serviços de terceiros. Um teste que falha à toa logo passa a ser ignorado.

### Smoke

Uma checagem **rápida e superficial** que responde: o sistema está de pé? O nome vem da eletrônica: liga-se o aparelho e vê se sai fumaça.

Roda logo após um build ou deploy, antes de qualquer verificação mais cara. Neste projeto, um smoke seria: o worker sobe, conecta no banco, encontra o schema esperado e registra uma execução sem erro.

| | Smoke | E2E |
| --- | --- | --- |
| Pergunta | Está funcionando o suficiente para continuar? | O fluxo está correto? |
| Profundidade | Só o caminho principal | Regras e resultados do fluxo |
| Duração | Segundos | Minutos |
| Se falhar | Interrompe o deploy ou faz rollback | Investiga o fluxo |

### Regressão

Garante que **o que já funcionava continua funcionando**. Regressão não é um nível como unitário ou E2E, e sim um **propósito**: qualquer teste da suíte protege contra regressões a cada execução.

O uso mais valioso é o **teste de regressão de um bug**:

1. Escreva um teste que reproduz o bug e veja-o **falhar**.
2. Corrija o código e veja-o passar.
3. Mantenha o teste para sempre.

O passo 1 é o mais importante. Um teste escrito depois da correção, sem nunca ter falhado, pode estar passando por outro motivo. O teste deve ficar no nível mais baixo que reproduza o bug e descrever a regra, não o incidente: `Leaves_out_boilerplate_related_links_and_page_chrome` diz mais do que `Fix_bug_123`.

### Mutação

Os outros tipos testam o código. A mutação **testa os testes**.

A ferramenta (no .NET, o Stryker.NET) faz pequenas alterações no código, cada uma gerando um **mutante**, e roda a suíte para cada um:

- **Mutante morto:** algum teste falhou, então a mudança foi percebida.
- **Mutante sobrevivente:** todos os testes passaram, então aquele comportamento pode quebrar sem ninguém notar.

```diff
- if (_knownStreak >= _knownStreakToStop) return StopWith(StopReason.ReachedKnownItems);
+ if (_knownStreak >  _knownStreakToStop) return StopWith(StopReason.ReachedKnownItems);
```

Esse mutante morre em `Stops_after_a_streak_of_known_articles`, porque a coleta passaria a parar um artigo depois do esperado.

Um sobrevivente mostra exatamente qual teste falta. Apagar a linha `_knownStreak = 0` só é detectado por `Known_articles_only_count_when_consecutive`. Sem um caso com conhecidos intercalados com novos, nada provaria que a sequência precisa ser **consecutiva**.

A mutação também expõe o limite da **cobertura de código**. Cobertura diz que a linha foi executada, não que algum teste conferiu o resultado. Uma linha pode ter 100% de cobertura e nenhuma verificação real.

- **Use para:** o núcleo de regras, onde um erro sutil custa caro.
- **Não use em:** todo commit ou no projeto inteiro, porque a suíte roda uma vez por mutante.
- **Cuidado:** alguns mutantes são **equivalentes**: mudam o código sem mudar o comportamento e nunca morrerão. Por isso a meta não é 100%.

Para rodar em uma camada:

```bash
dotnet tool install --global dotnet-stryker
cd tests/ArticleMonitor.UnitTests
dotnet stryker --test-runner mtp --project ArticleMonitor.Domain.csproj --output /tmp/stryker-domain
```

- `--test-runner mtp` é obrigatório: com o runner padrão, a captura de cobertura falha no xUnit v3 e o score sai perto de 0%, sem refletir os testes.
- O Stryker só considera a suíte em que foi executado. Um mutante coberto apenas pela integração aparece como sobrevivente ou sem cobertura na suíte unitária.
- Sobreviventes em guardas de nulo e mensagens de log raramente merecem teste. Priorize condições, limites e chamadas que mudam estado.

## Outros tipos úteis

### Por escopo

- **Contrato:** verifica se quem implementa uma interface cumpre as regras dela. Permite trocar ou adicionar implementações com segurança.
- **Componente:** testa um serviço inteiro com suas dependências externas simuladas. Fica entre a integração e o E2E.
- **Aceitação:** verifica um requisito do ponto de vista de quem usa, muitas vezes escrito junto com a pessoa que definiu o requisito.

### Por técnica

- **Snapshot (golden):** compara a saída com um resultado aprovado. Útil para saídas longas, como o texto extraído de uma página.
- **Property-based:** gera centenas de entradas e verifica uma propriedade que vale para todas, como "normalizar uma URL duas vezes dá o mesmo resultado que normalizar uma".
- **Fuzz:** envia entradas aleatórias ou malformadas para encontrar travamentos. Indicado para parsers de dados externos.
- **Arquitetura:** falha quando uma regra estrutural é violada, como a Application referenciar a Infrastructure.

### Por atributo de qualidade

- **Benchmark:** mede tempo e memória de um trecho de código, para comparar alternativas.
- **Carga:** mede o comportamento no volume esperado.
- **Estresse:** aumenta a carga até quebrar, para descobrir o limite e como o sistema se recupera.
- **Soak:** mantém o sistema rodando por horas ou dias, para achar vazamentos de memória e degradação.
- **Caos e resiliência:** derruba ou atrasa dependências de propósito, para verificar retries, timeouts e fallback.
- **Segurança:** análise do código (SAST), da aplicação em execução (DAST) e de vulnerabilidades em dependências (SCA).
- **Migração:** verifica se o schema evolui sem perder dados existentes.
- **Visual e acessibilidade:** detectam quebras de layout e barreiras de uso. Só fazem sentido com interface gráfica.
- **Monitoramento sintético:** executa um fluxo em produção de forma contínua e alerta quando ele falha.

## Testes do projeto

### Princípios

- **Tempo controlado:** tudo que depende de horário recebe `TimeProvider`, e os testes usam `FakeTimeProvider`. Assim, "a máquina dormiu por 16 horas" vira uma linha de código.
- **Fakes dos contratos:** os testes da Application não usam biblioteca de mock. Os fakes ficam em [Fakes.cs](../tests/ArticleMonitor.UnitTests/Application/Fakes.cs).
- **Fixtures sintéticas:** reproduzem a estrutura real dos sites com texto próprio, porque o texto das matérias tem direitos autorais ([detalhes](sources.md#fixtures)).
- **Banco isolado:** um container SQL Server por execução e um banco novo por teste, com as migrations reais aplicadas. O banco do Docker Compose nunca é tocado.
- **Sem sites reais:** nenhum teste acessa os portais de notícias, então nenhum falha porque um site saiu do ar.

### `ArticleMonitor.UnitTests`

| Classe | O que protege |
| --- | --- |
| `CanonicalUrlTests` | Normalização de URL, rejeição de URLs inválidas, mesmo hash para links do mesmo artigo e verificação do host da fonte |
| `CanonicalUrlPropertyTests` | **Property-based** (CsCheck): para URLs geradas, normalizar é idempotente, e parâmetros de rastreamento, ordem da query, caixa do host, fragmento e espaços não mudam a identidade |
| `DependencyRuleTests` | **Arquitetura**: cada camada só referencia as permitidas, o Domain só usa a biblioteca base, a Application não usa rede, banco ou biblioteca de site, e um módulo de fonte não depende de outro. Novos módulos entram na verificação sem alterar o teste |
| `SourceTests` | A marca d'água só avança, uma execução com falha não altera a fonte, e chaves são normalizadas e validadas |
| `CollectionRunTests` | Ciclo de vida da execução: status final, contadores, resumo de erros truncado, impossibilidade de fechar duas vezes e limite do alerta de conteúdo degradado |
| `CollectionScheduleTests` | Próximo e último horário, fuso configurado, virada de dia, execução iniciada exatamente no horário, e vários horários perdidos resultando em uma só coleta |
| `DiscoveryStopPolicyTests` | Limite de data, sequência de conhecidos, teto de itens, artigos sem data e detecção de possível lacuna |
| `ArticleContentReaderTests` | Ordem dos extratores: módulo, fallback genérico e falha, incluindo extrator que lança exceção |
| `CollectSourceTests` | Caso de uso completo com fakes: duplicatas entre feeds, links de outro host, página com erro, robots, falha de um feed, execução interrompida, erro inesperado, fim da leitura da listagem ao parar, preservação da marca d'água e emissão do alerta de conteúdo degradado |
| `RobotsTxtPolicyTests` | Interpretação do `robots.txt`: regra mais longa vence, `Allow` vence empate, grupos por user-agent e comentários |
| `CollectionSchedulerTests` | Recuperação ao iniciar, espera pelo próximo horário, máquina suspensa e nova tentativa quando o banco não está pronto |

### `ArticleMonitor.Sources.Testing`

Testes de **contrato** que todo módulo de fonte herda. Não rodam sozinhos: cada módulo fornece sua implementação e suas fixtures.

| Classe | Regras exigidas de toda fonte |
| --- | --- |
| `ArticleDiscovererContractTests` | URLs absolutas e títulos preenchidos, ordem do mais novo para o mais antigo, nenhuma requisição depois que o consumidor para, falhas reportadas sem exceção |
| `ArticleContentExtractorContractTests` | Extrai texto de uma página de artigo e devolve falha, sem exceção, para páginas sem artigo |

É isso que permite adicionar uma fonte sem revisar o núcleo: se o módulo passa no contrato, a regra de parada e o restante da coleta funcionam com ele.

### `ArticleMonitor.Sources.G1.Tests`

Herda os testes de contrato e adiciona o que é específico do G1.

| Classe | O que protege |
| --- | --- |
| `G1DiscoveryTests` | Leitura dos três feeds, datas com offset `-0000`, lead em `atom:subtitle`, GET condicional com ETag e preservação do ETag de um feed que falhou |
| `G1ArticleExtractorTests` | Ordem de parágrafos, intertítulos, listas e citações, remoção de boilerplate, URL canônica e falha em layout desconhecido, que aciona o extrator genérico |
| `G1SourceModuleTests` | Definição do módulo: chave, idioma, host e feeds |

### `ArticleMonitor.IntegrationTests`

Usa a composição real da aplicação no SQL Server 2025. Só a fonte, as páginas e o relógio são falsos.

| Classe | O que protege |
| --- | --- |
| `ArticleRepositoryTests` | Busca de artigos existentes por hash, inserções concorrentes do mesmo artigo e texto íntegro após a compressão Brotli |
| `CollectionCycleTests` | Registro único dos módulos, execuções presas viram `Interrupted`, ciclo de recuperação completo, fonte desativada, nenhuma coleta antes do próximo horário e segundo ciclo sem novos artigos |

### Regressões protegidas

| Comportamento que não pode voltar | Teste |
| --- | --- |
| Links de "leia também" e chamadas promocionais no texto do artigo, que gerariam match falso com palavras-chave | `Leaves_out_boilerplate_related_links_and_page_chrome` |
| Contador de descobertos incluindo itens listados depois da parada | `Stops_at_known_articles_without_fetching_them` |
| Campos internos serializados no estado de descoberta | `Saves_only_the_http_validators_in_the_state` |
| Remoção de todos os `?` iniciais da query, que daria identidades diferentes à mesma URL ao normalizar de novo | `Normalizing_twice_gives_the_same_url` e `Normalizes_the_url` |
| Artigos conhecidos não consecutivos encerrando a descoberta antes da hora | `Known_articles_only_count_when_consecutive` |

### Monitoramento da extração

As fixtures não percebem quando um site muda de layout, porque continuam iguais. Quem percebe é a própria coleta: ao fim de cada execução, o worker registra um `Warning` quando a parcela de artigos novos que usaram o fallback ou ficaram só com o lead passa de `ContentAlertRate`, em execuções com pelo menos `ContentAlertMinArticles` artigos novos.

O fallback entra na conta porque, após uma mudança de layout, o extrator genérico costuma recuperar o texto, com pior qualidade, e o artigo não vira `LeadOnly`.

### Verificação manual contra o G1 real

E2E e smoke ficam fora de `dotnet test`, porque dependem de rede e de um conteúdo que muda sozinho. Vale repetir este roteiro ao alterar o pipeline de coleta ou um extrator:

1. Com o ambiente do [Setup](../README.md#setup) pronto e o banco vazio, rode o worker. **Smoke:** ele sobe e registra uma execução por fonte em `CollectionRuns`.
2. **Primeira coleta:** o gatilho é `CatchUp`, há artigos em `Added` e nenhuma falha. Compare o texto de três artigos com a página no navegador.
3. **Reinício:** pare e suba o worker de novo. Nenhuma coleta deve rodar antes do próximo horário.
4. **Máquina desligada:** com o worker parado, recue `LastCompletedRunStartedAt` da fonte para o dia anterior e suba o worker. Deve haver um único `CatchUp`, sem artigos duplicados e com `PossibleGap = false`.
