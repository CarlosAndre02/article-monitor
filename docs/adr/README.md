# Architecture Decision Records

Cada ADR registra uma decisão de arquitetura: o contexto, o que foi decidido, as alternativas descartadas e as consequências. Os demais docs dizem como o sistema funciona; os ADRs guardam por quê.

## Quando escrever

Quando a decisão muda a estrutura do sistema, é cara de reverter e tinha uma alternativa real. A escolha de uma biblioteca pequena ou um detalhe de implementação não entram.

## Regras

- Os ADRs são a exceção à regra de não registrar estado nem histórico: cada um tem data e status e descreve o contexto em que a decisão foi tomada.
- Um ADR aceito não é reescrito. Se a decisão mudar, um ADR novo a substitui, e no antigo só o status muda, para `Substituído por NNNN`.
- Status possíveis: `Proposto` (premissa a validar), `Aceito`, `Rejeitado` e `Substituído por NNNN`.
- Um item de "Decisões em aberto" em [requirements.md](../requirements.md) vira um ADR quando é decidido.
- As análises que embasam um ADR também são datadas e ficam em [analyses/](analyses/).

## Modelo

```markdown
# NNNN. Decisão, em uma frase

- **Status:** Proposto
- **Data:** AAAA-MM-DD

## Contexto

## Decisão

## Alternativas consideradas

## Consequências
```

## Índice

| ADR | Decisão | Status |
| --- | --- | --- |
| [0001](0001-stack-dotnet-sql-server.md) | .NET 10 com SQL Server 2025 Express | Aceito |
| [0002](0002-tres-processos-e-um-banco.md) | Worker, API e bot como processos separados, com o banco entre worker e API | Proposto |
| [0003](0003-agenda-global-com-recuperacao.md) | Agenda global em horários fixos, com recuperação ao iniciar | Aceito |
| [0004](0004-fontes-como-modulos-em-um-projeto.md) | Fontes como módulos atrás de contratos, num único projeto | Aceito |
| [0005](0005-regra-de-parada-comum.md) | Regra de parada da descoberta comum a todas as fontes | Aceito |
| [0006](0006-matching-na-aplicacao.md) | Matching na aplicação, sem full-text search do banco | Aceito |
| [0007](0007-nuvem-de-termos-no-worker.md) | Nuvem de termos gerada pelo worker | Proposto |
| [0008](0008-composicao-do-heat.md) | Heat composto por score lexical e similaridade por embedding | Proposto |
