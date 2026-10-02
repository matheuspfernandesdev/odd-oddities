# Architecture Decision Record - Selecao Dinamica de Modelos com Fallback e Budget

## Status

Aceito

## Contexto

Os modelos do OpenRouter eram hardcoded em configuracao (`TextModelId`/`ImageModelId`). O modelo de imagem `meta/muse-image` saiu do catalogo e a execucao falhava sem fallback. O catalogo do OpenRouter expoe `GET /api/v1/models` com precos e modalidades de saida, permitindo descoberta em runtime. Free models rotacionam com frequencia, tornando listas fixas rapidamente obsoletas.

## Decisao

1. **Catalogo dinamico por execucao**: no inicio de cada execucao do pipeline, `ModelSelectionService` busca `GET /models?output_modalities=text` e `GET /models?output_modalities=image` (cache no DI scope = 1 busca/execucao). Falha na busca -> cadeia apenas com o modelo da config (pipeline nunca quebra por isso).
2. **Cadeia preferido + dinamica**: primeiro o modelo da config (`TEXT_MODEL_ID`/`IMAGE_MODEL_ID`), depois candidatos ordenados free -> mais barato, filtrados por contexto minimo e teto de custo por request.
3. **Fallback por erro de modelo**: `OpenRouterModelException` (HTTP nao-sucesso, resposta malformada, modelo invalido) ou excecoes de transporte avancam para o proximo candidato, ate `MaxTextModelAttempts` (5) / `MaxImageModelAttempts` (3) modelos distintos.
4. **Retry de conteudo no mesmo modelo**: rejeicoes de negocio (BR-002/BR-004/BR-005) re-tentam no mesmo modelo ate `MaxGenerationAttempts` (3) — comportamento pre-existente preservado.
5. **Budget por execucao**: `PipelineContext.AccumulatedCostUsd` acumula custo real (`usage.cost`); antes de cada chamada estima-se o custo e compara com `MaxCostPerRunUsd` (default USD 0,05). Estourou -> `BUDGET_EXCEEDED`.
6. **Auditoria**: cada tentativa grava em `GenerationAttempt` (`PostId` anulavel para tentativas de texto anteriores a criacao do Post).
7. Endpoint de imagem: `POST /api/v1/images` (o antigo `images/generations` nao e o endpoint oficial).

Config na secao `AppConfiguration:ModelSelection` (defaults no codigo).

## Consequencias

**Positivas**

- Fallback automatico quando um modelo some do catalogo ou falha.
- Sem manutencao de listas fixas de fallback.
- Custo controlado por execucao, com trilha de auditoria no banco.
- Catalogo indisponivel degrada para o comportamento antigo.

**Negativas**

- Uma chamada HTTP extra por execucao (catalogo, ~750KB).
- Custo e estimativa antes da chamada podem divergir do real (corrigido apos o fato via `usage.cost`).
- Candidatos pagos dependem da qualidade da estimativa de tokens (`EstimatedPromptTokens`/`EstimatedCompletionTokens`).

## Alternativas consideradas

- **Lista fixa de fallback na config**: rejeitada — catalogo free rotaciona e a lista envelhece.
- **Totalmente dinamico (sem preferido)**: rejeitada — mantem controle explicito do modelo principal.
- **Retry com backoff exponencial (ADR-007)**: nao implementado; substituido na pratica por fallback de modelo (um novo candidato e uma nova tentativa).
- **Budget diario/mensal**: adiado — budget por execucao atende a POC.

## Relacionados

- [ADR-007 Retry com Backoff](./ADR-007-retry-backoff.md) (status: nao implementado, ver ADR-008)
- [ADR-004 Clientes Separados](./ADR-004-openrouter-clientes-separados.md)
