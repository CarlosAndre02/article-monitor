# Requisitos

O que o article-monitor precisa fazer e por quê. Como cada feature funciona está em [features/](features/), o modelo de domínio em [project.md](project.md), e as decisões de arquitetura, com as alternativas descartadas, em [adr/](adr/README.md).

## Problema

Acompanhar notícias sobre temas de interesse exige ler vários portais várias vezes ao dia. O article-monitor coleta os artigos sozinho, mede a relevância (heat) de cada um para cada palavra-chave cadastrada e permite consultar os resultados pelo Discord.

## Uso

- Uso pessoal, com volume estimado de ~1.000 artigos por dia.
- Os resultados são consultados sob demanda, por slash commands.
- Roda local ou na nuvem sem mudança de código.

## Fora do escopo

- **Texto completo no Discord**: o texto das matérias tem direitos autorais. O resultado traz link, título e um trecho curto.
- **Conteúdo atrás de paywall**: raspar esse conteúdo viola os termos de uso. Dessas fontes, só entra o que a API oficial entrega (título, resumo e lead).
- **Corte por relevância na coleta**: nenhum artigo é descartado por heat baixo. O limiar é um parâmetro da consulta, então um limiar mal escolhido não perde dados.
- **Detecção automática de idioma**: o idioma vem da definição da fonte.

## Requisitos

### R1. Coleta

Coletar os artigos novos de cada fonte várias vezes ao dia, sem perder artigos quando a máquina passa um tempo desligada.

Critérios de aceite:
- Um horário perdido com a máquina desligada resulta em uma coleta assim que ela volta. Vários horários perdidos resultam em uma só.
- Cada execução por fonte fica registrada: status, artigos novos, motivo de parada e erros. Sem isso, um feed quebrado ou um extrator que parou de funcionar passa despercebido.
- Uma execução que pode ter perdido artigos fica marcada como possível lacuna.
- A coleta respeita o `robots.txt` e o rate limit por domínio e se identifica pelo User-Agent.
- A coleta só acessa as fontes por HTTPS e nunca acessa endereços da rede local, nem quando uma fonte redireciona para eles. As URLs vêm de fora, e uma fonte comprometida não pode usar o coletor para ler a máquina ou a rede onde ele roda.
- Uma fonte com problema não atrapalha a coleta das outras.

### R2. Fontes

- G1, nas editorias de política, economia e tecnologia.
- Fontes em outros idiomas, como o NYT em inglês. Fontes com paywall entram pela API oficial, com conteúdo parcial.
- Artigo com conteúdo parcial é um caso normal: ele continua consultável pelo título e pelo lead.
- Adicionar uma fonte não exige mudar as que já existem ([sources.md](sources.md)).

### R3. Identidade do artigo

O mesmo artigo é gravado uma vez, mesmo quando aparece em mais de um feed ou com parâmetros de rastreamento (`utm_*`) diferentes.

### R4. Palavras-chave

Critérios de aceite:
- Ao cadastrar uma palavra, o sistema gera a nuvem de termos dela: sinônimos, termos relacionados e termos negativos, cada um com um peso.
- A nuvem pode ser vista e editada pelo Discord.
- A ambiguidade de siglas ("IA" e o verbo "ia") se resolve com termos negativos, cadastrados por quem cadastra a palavra.
- Palavras, termos e pesos vindos do Discord são normalizados e validados antes de serem gravados.
- Para fontes em mais de um idioma, a nuvem inclui termos em cada idioma monitorado.

### R5. Relevância (heat)

Critérios de aceite:
- O heat pertence ao par artigo × palavra-chave, não ao artigo.
- Cada resultado explica o heat com os termos que deram match. Exemplo: "heat 65%: IPCA no título, Copom, juros".
- Uma palavra nova é avaliada também contra os artigos já coletados (match retroativo).
- Mudar o algoritmo de score ou editar a nuvem de uma palavra permite reprocessar os artigos.
- Um termo genérico ou um link de "leia também" no rodapé não basta para um heat alto.

### R6. Consulta pelo Discord

| Comando | O que faz |
| --- | --- |
| `/palavra add` | Cadastra uma palavra-chave e gera sua nuvem |
| `/palavra listar` | Lista as palavras cadastradas |
| `/palavra nuvem` | Mostra e edita os termos e pesos da nuvem |
| `/buscar <palavra> min:<heat>` | Lista os artigos acima do limiar |

Critérios de aceite:
- Cada resultado traz link, título, heat, termos que deram match e um trecho curto.
- Os comandos funcionam sem o intent privilegiado de Message Content.
- Os nomes dos comandos não conflitam com os de outros bots no mesmo servidor.
- Título e trecho vêm das fontes sem tratamento, então são texto não confiável. Antes de exibidos, o markdown é escapado e os caracteres de controle e de direção de texto (bidi) são removidos, e as mensagens saem com `allowed_mentions` vazio. Sem isso, uma matéria poderia mencionar `@everyone`, esconder um link atrás de outro texto ou embaralhar a mensagem.

O Discord foi escolhido porque canais e threads organizam o histórico por tema, e slash commands e embeds cobrem a interação. O Telegram não tem canais para essa organização. No WhatsApp, a API oficial é voltada a empresas, e as bibliotecas não oficiais arriscam banimento.

### R7. Feedback

- Os resultados do `/buscar` têm botões 👍/👎. Botões, e não reações, porque chegam ao bot como interação.
- O feedback é guardado para recalibrar pesos e limiar com dados reais.

### R8. Retenção

O banco ocupa no máximo 1 GB. A retenção é por tempo, e não pelos "N últimos registros", porque as fontes têm volumes muito diferentes.

| Artigos | O que fica | Por quanto tempo |
| --- | --- | --- |
| Relevantes (heat acima do limiar ou com 👍) | Tudo | Para sempre |
| Demais, dentro da janela | Texto, embedding e metadados | 60 dias; é onde o match retroativo funciona |
| Demais, após a janela | Só metadados | Enquanto a deduplicação precisar deles |

- Após a janela, o match retroativo de uma palavra nova só alcança o título.
- O limiar de relevância da retenção vem de configuração.
- Os metadados não podem ficar para sempre: crescem ~180 MB por ano e acabariam estourando a meta. A deduplicação só precisa do hash enquanto o artigo pode reaparecer num feed.
- A estimativa de tamanho por artigo está em [analyses/stack-dotnet.md](adr/analyses/stack-dotnet.md#armazenamento).

## Métricas

| Métrica | Onde observar | Meta |
| --- | --- | --- |
| Execuções com falha ou possível lacuna | Tabela `CollectionRuns` | A definir com dados reais |
| Artigos sem texto extraído (fallback ou `LeadOnly`) | Alerta de conteúdo degradado no log | Abaixo de `ContentAlertRate` |
| Tamanho do banco | SQL Server | Até 1 GB |
| Proporção de 👍 nos resultados do `/buscar` | Feedback gravado | A definir com dados reais |

## Decisões em aberto

Cada uma vira um [ADR](adr/README.md) quando for decidida.

- Provedor do LLM e modelo de embedding.
- Hospedagem (VPS, servidor local, Docker).
- Fontes e feeds além do G1.
- Por quanto tempo manter os metadados após a janela de retenção.
- Como combinar o score lexical e a similaridade por embedding no heat ([ADR 0008](adr/0008-composicao-do-heat.md)).
