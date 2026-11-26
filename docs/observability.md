# Observabilidade

Como saber o que o worker fez e por que algo deu errado. Atende o [R9](requirements.md#r9-operação).

A tabela `CollectionRuns` é o registro de cada execução: status, motivo de parada, possível lacuna, contadores e o resumo dos erros. Os logs completam esse registro com o detalhe de cada artigo, página e requisição.

## Logs

Todo log é escrito com `[LoggerMessage]`, então os valores da mensagem (`{Source}`, `{PageUrl}`, `{Status}`) viram campos estruturados, pesquisáveis sem depender do texto.

### Escopo da execução

`CollectSource` abre um escopo de log com `Source` (a chave da fonte) e `RunId` (o `Id` da linha em `CollectionRuns`) assim que a execução é gravada. Todo log escrito durante a execução leva esses dois campos, venha de que classe vier: extração, robots.txt, pipeline HTTP. Assim, a partir de uma linha de `CollectionRuns` com falha, os logs dela são encontrados por `RunId`.

O escopo acompanha o fluxo assíncrono, então as fontes coletadas em paralelo não misturam os campos. Logs fora de uma execução, como os da agenda e do preparo, não têm esses campos.

### Destino

[Telemetry.cs](../src/ArticleMonitor.Worker/Telemetry.cs):

- **Console**: sempre.
- **Coletor OTLP**: quando `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado. Os logs vão com os campos estruturados e os do escopo. As demais configurações seguem as variáveis padrão do OpenTelemetry, como `OTEL_EXPORTER_OTLP_PROTOCOL`.
- Sem o endpoint, o worker roda igual, só com o console.

Em `Development`, o [appsettings.Development.json](../src/ArticleMonitor.Worker/appsettings.Development.json) aponta para o Seq do `compose.yaml`. Fora dele, o endpoint vem de variável de ambiente. O OTLP é um padrão, então trocar o Seq por outro coletor muda só a configuração.

Se o coletor estiver fora do ar, os logs que iriam para ele se perdem, sem afetar a coleta.

## Seq

Sobe com o `docker compose up -d`, em `127.0.0.1:5341`.

- **Interface**: http://localhost:5341, usuário `admin`. A senha vem de `SEQ_ADMIN_PASSWORD`, no `.env`, e só é lida quando o volume está vazio. Depois disso, ela é trocada no próprio Seq.
- **Ingestão**: OTLP por HTTP/protobuf em `/ingest/otlp`. Não pede API key, por isso a porta só escuta em `127.0.0.1`.
- **Retenção**: o Seq guarda tudo até que uma política de retenção seja criada nas configurações dele. Sem ela, o volume cresce sem limite.

Consultas úteis, combinadas com o filtro de nível da interface:

| Pergunta | Consulta |
| --- | --- |
| O que aconteceu na execução 42? | `RunId = 42` |
| Tudo de uma fonte | `Source = 'g1'` |
| Extrações lentas | `@Message like 'Extractor % took %'` |
| Páginas de um host | `PageUrl like 'https://g1.globo.com/%'` |
