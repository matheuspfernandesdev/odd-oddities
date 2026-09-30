# Architecture Decision Record - Pipeline Alternativo de Video com Steps Skip-able

## Status

Aceito

## Contexto

O MVP 2 introduziu a geracao de video (Reels ≤ 8 s, 9:16, 480p) publicado 1 vez a cada 15 dias. O requisito do dono foi explicito: "ao acionar cada execucao deve identificar se e uma execucao que ira gerar video ou imagem. Se for imagem, executa o ImageStep e pula o VideoStep, se for video faz o contrario."

O pipeline atual do `PipelineOrchestrator` e uma lista declarativa e ordenada de `IPipelineStep` percorrida por um `foreach`, com uma unica trava (`SemaphoreSlim(1,1)`) garantindo execucao serial. Video e imagem sao exclusivos por execucao — nunca ambos — porque cada execucao publica exatamente uma midia (BR-012 atualizada). Precisavamos encaixar a decisao video/imagem sem duplicar orchestrator, scheduler ou lock, e sem quebrar o `foreach` sequencial.

Ao mesmo tempo, o outcome de `StepResult` so tinha `Success`/`Failure`. Um step que "nao se aplica" a uma execucao (o `ImageGenerationStep` numa execucao de video, ou o `VideoGenerationStep` numa execucao de imagem) nao podia ser distinguido nos logs nem impedido de marcar o Post como `Failed`.

## Decisao

1. **Decisao de modalidade resolvida uma vez no orchestrator**: `bool isVideoRun = ISchedulerPort.IsVideoRunToday()` e chamado uma unica vez no inicio da execucao e gravado em `PipelineContext.IsVideoRun`. A regra: busca a `PublishedAt` do ultimo Post com `VideoObjectKey != null`; se `hoje - lastVideoPublishedAt >= Video.IntervalDays` (15) ou nunca houve video, e dia de video. Falha na query → `false` (fallback para o comportamento de imagem).
2. **Steps de midia com short-circuit (`StepResult.Skipped()`)**: `ImageGenerationStep` e `VideoGenerationStep` fazem `if (context.IsVideoRun != <minha modalidade>) return StepResult.Skipped();`. A ordem declarativa e o `foreach` do orchestrator permanecem intactos, com uma unica trava.
3. **Novo outcome `Skipped`**: `StepResult.Skipped()` tem `IsSuccess = true` e `FailureStep = null`. O orchestrator loga `outcome = Skipped` (distinto de `Success`) e segue para o proximo step. `Skipped` **nunca** marca o Post como `Failed`.
4. **Ordem final do pipeline (5 steps)**: `CommentSuggestionStep` → `TextGenerationStep` → `ImageGenerationStep` → `VideoGenerationStep` → `PublicationStep`. Os steps de midia skipam conforme a modalidade; o `PublicationStep` escolhe imagem (`CreateMediaContainerAsync`) vs Reels (`CreateReelsContainerAsync`) pelo `context.IsVideoRun`, com polling de container mais longo para video.
5. **Budget por modalidade resolvido no contexto**: `PipelineContext.CostCeilingUsd` recebe `MaxCostPerVideoRunUsd` (0.20) quando `IsVideoRun`, senao `MaxCostPerRunUsd` (0.06). Cada step compara contra `context.CostCeilingUsd` em vez de decidir por conta propria (ver ADR-008 para a mecanica de cadeia/budget).
6. **PostStatus reusado**: `ImageProcessed` e reusado para "media processed" no fluxo de video (sem migration de enum). Renomear fica como refactor futuro.

## Consequencias

**Positivas**

- Uma unica implementacao de orchestrator, scheduler e lock; superficie de bug menor.
- Ordem do pipeline permanece declarativa e legivel; adicionar/remover step nao muda o controle de fluxo.
- Logs distinguem `Skipped` de `Success`, tornando a decisao de modalidade auditavel.
- Budget correto por modalidade sem cada step reimplementar a regra.

**Negativas**

- Dois steps de midia sempre presentes na lista, um deles sempre skipando por execucao (custo trivial de um short-circuit).
- Reuso semantico de `ImageProcessed` para video e uma pequena impropriedade de dominio (documentada; risco R21).
- `IsVideoRunToday()` depende de uma query extra ao banco por execucao (leve, com fallback seguro).

## Alternativas consideradas

- **Dois pipelines/threads separados**: rejeitada — duplica orchestrator, scheduler e lock; maior superficie de bug; viola "single pipeline execution at a time" (`SemaphoreSlim`).
- **Branching dentro do orchestrator**: rejeitada — logica de desvio no orchestrator conflita com o desenho atual de steps independentes e sequenciais.
- **`StepResult.Skipped()` com `IsSuccess = true` sem outcome explicito**: rejeitada — seria logado como `Success`, e o criterio de aceite "loga skip" nao seria verificavel; por isso o outcome ganhou representacao propria.

## Relacionados

- [ADR-008 Selecao Dinamica de Modelos com Fallback e Budget](./ADR-008-modelo-dinamico-fallback-custo.md)
- [ADR-010 Sugestoes de Tema via Comentarios com Fallback de Permissao](./ADR-010-sugestoes-comentarios-fallback-permissao.md)
- [ADR-002 Hexagonal (Ports and Adapters)](./ADR-002-hexagonal.md)
