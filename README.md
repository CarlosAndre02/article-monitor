# article-monitor

...

## Requirements

- .NET SDK 10 (pinned in `global.json`)
- Docker: SQL Server 2025 Express runs through Docker Compose, and the integration tests use Testcontainers.

## Setup

```bash
cp .env.example .env    # then set MSSQL_SA_PASSWORD
docker compose up -d    # SQL Server on 127.0.0.1:1433 only
dotnet tool restore     # dotnet-ef

dotnet user-secrets set "ConnectionStrings:ArticleMonitor" \
  "Server=localhost,1433;Database=ArticleMonitor;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True" \
  --project src/ArticleMonitor.Worker

dotnet ef database update --project src/ArticleMonitor.Infrastructure --startup-project src/ArticleMonitor.Worker
```

Outside development, set the connection string with the `ConnectionStrings__ArticleMonitor` environment variable.

## How to test

```bash
dotnet test                                                                     # every suite
dotnet test --project tests/ArticleMonitor.UnitTests                            # one suite
dotnet test --project tests/ArticleMonitor.UnitTests --filter-class "*DiscoveryStopPolicyTests"
dotnet test --project tests/ArticleMonitor.UnitTests --filter-method "*Stops_at_known_articles*"
```

| Suite | Covers | Needs |
| --- | --- | --- |
| `ArticleMonitor.UnitTests` | Domain rules, URL properties, architecture rules, use cases with fakes of the contracts, robots.txt parsing, scheduler | Nothing |
| `ArticleMonitor.Sources.Tests` | Each source module's discovery and extraction on synthetic fixtures, plus the contract tests every module inherits | Nothing |
| `ArticleMonitor.IntegrationTests` | Repositories and full collection cycles on SQL Server 2025 | Docker |

The integration tests start their own SQL Server container (Testcontainers) and create a fresh database per test; they never touch the Docker Compose database. The first run pulls the SQL Server image, which takes a few minutes.

Fixtures reproduce the structure of the real sites with made-up text, because article text is copyrighted ([docs/sources.md](docs/sources.md#fixtures)).

## How to run

```bash
dotnet run --project src/ArticleMonitor.Worker
```

The worker collects at the times in `Collector:Schedule` (`appsettings.json`). When it starts and a scheduled time passed while it was off, it collects once right away. Every run is recorded in the `CollectionRuns` table: counters, why discovery stopped, a possible-gap flag and errors.

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d ArticleMonitor \
  -Q "SELECT TOP 5 Id, [Trigger], Status, StopReason, PossibleGap, Added, Duplicates, Failures FROM CollectionRuns ORDER BY Id DESC"
```

## How to deploy

## Architecture

Clean Architecture: inner layers define contracts, outer layers implement them.

```
src/
  ArticleMonitor.Domain/           Entities and pure rules: Source, Article, CanonicalUrl, CollectionRun, CollectionSchedule
  ArticleMonitor.Application/      Use cases, discovery stop policy and contracts (ISourceModule, IArticleDiscoverer,
                                   IArticleContentExtractor, IPageFetcher, repositories)
  ArticleMonitor.Infrastructure/   EF Core + SQL Server persistence and the shared HTTP pipeline
                                   (resilience, per-host rate limit, robots.txt, response size limit)
  ArticleMonitor.Sources/          Source modules, one folder each
    Common/                        Reusable building blocks (RSS discoverer, generic readability extractor)
    G1/                            G1 module: definition (host, language, feeds) and G1-specific extractor
  ArticleMonitor.Worker/           Composition root and the scheduled collection service
tests/
  ArticleMonitor.UnitTests/        Domain and Application, using fakes of the contracts
  ArticleMonitor.Sources.Tests/    Contract tests every source module inherits (Contracts/), plus one folder per module
  ArticleMonitor.IntegrationTests/ Full collection cycle against SQL Server (Testcontainers)
```

Dependency rule: `Domain ← Application ← Infrastructure, Sources ← Worker`. The Application layer never references I/O or site-specific code.

### Adding a news source

Each source is an isolated module (a `Sources/<Name>` folder, namespace `ArticleMonitor.Sources.<Name>`) that implements `ISourceModule` and registers itself with one extension method (e.g. `AddG1Source()`) in the Worker. A new source needs:

1. A `Sources/<Name>` folder with its discoverer and extractor.
2. A `Sources.Tests/<Name>` folder with tests that inherit the contract tests from `Contracts/`.
3. One registration line in the Worker.

Existing modules and the core stay untouched, so their tests keep guarding what already works. Modules share one project so the number of projects does not grow with the sources; an architecture test, instead of the compiler, fails when a module uses another one. Robots.txt, rate limiting and resilience live in the shared HTTP pipeline, so no module can bypass them. Details in [docs/sources.md](docs/sources.md).