# 0004. Fontes como módulos atrás de contratos, num único projeto

- **Status:** Aceito
- **Data:** 2026-11-24

## Contexto

Novas fontes (CNN Brasil, NYT) serão adicionadas ao longo do tempo. Adicionar uma fonte não pode exigir mudar as existentes nem o núcleo. As regras comuns, como agenda, deduplicação, regra de parada, persistência e politeness HTTP, precisam valer igual para todas, sem depender de cada fonte lembrar delas.

## Decisão

- Cada fonte é um módulo que implementa `ISourceModule`: uma definição (chave, idioma, hosts), um discoverer e um extrator. O resto é do núcleo.
- Os adapters são por função, não por site: discoverer genérico de RSS/Atom, extrator genérico como fallback e código específico só onde o genérico falha.
- Todos os módulos são pastas do projeto `ArticleMonitor.Sources`, com os blocos comuns em `Common/`. Os testes ficam em `ArticleMonitor.Sources.Tests`, com os testes de contrato que todo módulo herda.
- Um teste de arquitetura (`SourceIsolationTests`, com ArchUnitNET) lê as dependências do código compilado e falha quando um módulo usa outro, ou quando `Common/` usa um módulo.

Como adicionar uma fonte: [sources.md](../sources.md).

## Alternativas consideradas

- **Um projeto por fonte**: o compilador garantiria o isolamento, e o `internal` de uma fonte seria invisível às outras. Com 20 fontes, porém, seriam cerca de 40 projetos contando os de teste, com build mais lento e csproj repetido.
- **Um adapter específico por site para tudo**: cada fonte reimplementaria descoberta e extração, mesmo quando um RSS e o extrator genérico bastam.

## Consequências

- Uma fonte nova é uma pasta de código, uma pasta de testes e uma linha de registro no Worker.
- O isolamento entre módulos depende de um teste, não do compilador. Uma violação aparece no `dotnet test`, não no build.
- Nenhum módulo consegue pular robots.txt, rate limit ou resiliência, porque eles ficam no pipeline HTTP compartilhado.
