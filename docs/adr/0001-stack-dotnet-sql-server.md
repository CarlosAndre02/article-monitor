# 0001. .NET 10 com SQL Server 2025 Express

- **Status:** Aceito
- **Data:** 2026-11-08

## Contexto

As premissas iniciais foram pensadas para PostgreSQL: full-text search com `unaccent` e `portuguese`, `tsvector` armazenado e pgvector. A stack .NET foi avaliada quanto à viabilidade e ao custo para uso pessoal, com ~1.000 artigos por dia e o banco limitado a 1 GB, rodando local ou numa VPS barata. A análise completa está em [analyses/stack-dotnet.md](analyses/stack-dotnet.md).

## Decisão

- .NET 10 (LTS) e EF Core 10.
- SQL Server 2025 Express, em Docker no desenvolvimento e na execução local.

## Alternativas consideradas

- **PostgreSQL**: tem full-text search e pgvector prontos e roda em ARM, então a VM gratuita da Oracle (Ampere A1) hospedaria tudo, banco incluído.
- **Azure SQL Database free tier no lugar do Express**: gratuito, mas a cota de compute limita a coleta a poucas execuções por dia e o banco pausa até o mês seguinte se ela estourar.

## Consequências

- O SQL Server só roda em x64 e exige 2 GB de RAM, o que exclui as VMs gratuitas mais generosas. A hospedagem fica entre uma máquina própria e uma VPS x64 (~€ 6/mês).
- O full-text search do SQL Server não é usado, e o matching vai para a aplicação ([ADR 0006](0006-matching-na-aplicacao.md)).
- O tipo `VECTOR` nativo do SQL Server 2025 substitui o pgvector.
- O Express não tem o SQL Server Agent, então jobs periódicos, como a retenção, rodam no worker.
- A extração em .NET (SmartReader) é mais fraca que o trafilatura do Python. A mitigação são extratores específicos por fonte com AngleSharp, o extrator genérico como fallback e o alerta de conteúdo degradado.
