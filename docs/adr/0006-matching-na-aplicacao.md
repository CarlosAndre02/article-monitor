# 0006. Matching na aplicação, sem full-text search do banco

- **Status:** Aceito
- **Data:** 2026-11-08

## Contexto

O matching precisa ignorar acentos e caixa, respeitar o limite de palavra ("IA" não bate em "média") e aplicar stemming ("eleição" e "eleições"), em português e inglês. Com o SQL Server ([ADR 0001](0001-stack-dotnet-sql-server.md)), isso viria do full-text search do banco. O score, porém, já exige código próprio: posição do termo, termos distintos, pesos e termos negativos.

## Decisão

- Normalização e stemming na aplicação, com os analisadores do `Lucene.Net.Analysis.Common`. O analisador é escolhido pelo idioma da definição da fonte.
- O texto do artigo é gravado comprimido com Brotli pela aplicação, sem índice full-text.

## Alternativas consideradas

- **Full-text search do SQL Server**:
  - o índice é populado de forma assíncrona, então um artigo recém-inserido não aparece na busca, e o worker teria de esperar para pontuar;
  - no Linux e no Docker, exige o pacote `mssql-server-fts`, ou seja, uma imagem customizada;
  - resolveria só acentos e stemming, e o restante do score continuaria na aplicação.

## Consequências

- A regra de score fica coberta por testes de unidade, sem banco.
- O texto ocupa cerca de um terço do tamanho em UTF-8, e o servidor de banco não gasta CPU com compressão.
- O match retroativo de uma palavra nova relê em lotes os artigos da janela de retenção (~60 mil). A estimativa é de poucos minutos por palavra, a validar.
- O Lucene.NET 4.8 ainda tem rótulo beta. O uso fica restrito aos analisadores, atrás de uma interface.
- O idioma vem da definição da fonte, sem detecção automática.
