# Planejamento MVP 2 — Odd Oddities

> Planejamento do MVP 2: (1) geração e publicação de vídeos Reels a cada 15 dias via OpenRouter; (2) leitura de comentários dos últimos 15 posts e publicação de sugestões de tema com crédito ao autor.
>
> Documentos relacionados: [`architecture.md`](./architecture.md), [`openrouter.md`](./openrouter.md), [`instagram-api.md`](./instagram-api.md), [`adr/`](./adr/).

---

# 1. Resumo executivo

| Item | Decisão |
|---|---|
| Vídeo | Pipeline alternativo (não paralelo): cada execução do `PipelineOrchestrator` decide se gera **imagem** ou **vídeo** (Reels ≤ 8 s, 9:16, 480p). |
| Freq. vídeo | 1 vídeo a cada 15 dias (config `SCHEDULE_VIDEO_INTERVAL_DAYS=15`). |
| Modelos de vídeo | Cadeia dinâmica via `GET /api/v1/videos/models` (padrão ADR-008): free primeiro (hoje não existe), depois mais barato por segundo, com tetos de custo. |
| Custo/vídeo | Alvo ≤ USD 0,15. Melhor candidato: `bytedance/seedance-2.0-mini` (~USD 0,05–0,11). |
| Publicação Reels | Mesma Meta Graph API já usada (`graph.instagram.com/v26.0`), com `media_type=REELS&video_url=...`. Nenhuma permissão nova necessária. |
| Comentários | Novo step `CommentSuggestionStep` (primeiro do pipeline): lê comentários dos últimos 15 posts, IA classifica sugestões, sugestão válida substitui o tema do dia e vira post normal com crédito `Suggested by @user`. |
| Permissão nova | `instagram_business_manage_comments` — **o token atual não a tem**. Sem ela, o step é pulado com warning (fallback planejado). |
| Custo mensal incremental | ~USD 0,15–0,40 (2 vídeos) + ~USD 0 (comentários, texto em modelo free). |
| Risco principal | Permissão de comentários (regeneração de token manual) e preço/qualidade de vídeo sem modelo gratuito. |

---

# 2. Análise de viabilidade — Geração de vídeo

## 2.1 OpenRouter Video Generation (verificado em 2026-09)

A OpenRouter lançou geração de vídeo em abril/2026. Documentação oficial: `openrouter.ai/docs/guides/overview/multimodal/video-generation`.

**API assíncrona em 4 passos** (diferente de texto e imagem, que são síncronos):

```text
1. POST /api/v1/videos            → 202 { id, polling_url, status: "pending" }
2. GET  /api/v1/videos/{jobId}    → { status: "pending" | "in_progress" | "completed" | "failed" }
3. GET  /api/v1/videos/{jobId}/content?index=0   (com Bearer token) → MP4
4. usage.cost vem no poll response quando completed
```

**Parâmetros relevantes**: `model`, `prompt`, `duration` (s), `resolution` (`480p`…`4K`), `aspect_ratio` (`9:16` vertical — necessário para Reels), `generate_audio` (default `true`).

**Descoberta de modelos**: `GET /api/v1/videos/models` retorna 29 modelos com `supported_durations`, `supported_resolutions`, `supported_aspect_ratios` e `pricing_skus` (preço **por segundo de vídeo**, não por token/imagem). Também há `GET /api/v1/models?output_modalities=video`, mas o endpoint dedicado é mais completo (durations/resolutions por SKU).

## 2.2 Levantamento de preços (text-to-video, 9:16, ≤ 8 s)

Verificado contra a API em 2026-09 (`/api/v1/videos/models`). **Não existe nenhum modelo de vídeo gratuito** no catálogo (confirmado: zero SKUs a `0`).

| Modelo | SKU mais barato | 9:16 | Durações | Custo estimado 5–8 s |
|---|---|---|---|---|
| `bytedance/seedance-2.0-mini` | USD 3,5e-6/video-token | ✅ | 4–15 s | **~USD 0,05–0,11** |
| `bytedance/seedance-2.0` | USD 7e-6/video-token | ✅ | 4–15 s | ~USD 0,10–0,22 |
| `bytedance/seedance-2.0-fast` | USD 4,2e-6/video-token | ✅ | 4–15 s | ~USD 0,06–0,13 |
| `alibaba/wan-3.0` | 0,05/s (480p) | ✅ | 2–30 s | USD 0,25–0,40 |
| `google/veo-3.1-lite` | 0,03/s (720p sem áudio) | ✅ | 4/6/8 s | USD 0,15–0,24 |
| `kwaivgi/kling-v3.0-std` | 0,084/s (720p) | ✅ | 3–15 s | USD 0,42–0,67 |

⚠️ **Atenção aos Seedance (cobrança por video-token)**: os SKUs `video_tokens` cobram por token, não por segundo. O valor exato depende do denominador interno (não documentado). Com o denominador usual do Seedance (~1.088 tokens/s em 480p), `seedance-2.0-mini` fica em ~USD 0,02/s → ~USD 0,10 por vídeo de 5 s. **Ação de implementação: na primeira execução real, comparar `usage.cost` retornado com a estimativa e ajustar o estimador** (`ModelCostEstimator`). O `usage.cost` real no poll response é a fonte da verdade e já é acumulado no budget.

**Recomendação**: cadeia preferida = `bytedance/seedance-2.0-mini` (480p, 5 s ou 8 s, 9:16, com áudio). Se o custo real estourar o teto, o fallback dinâmico pega o próximo mais barato.

## 2.3 Conformidade com Reels (Meta — especificações oficiais, verificadas 2026-09)

Fonte: `developers.facebook.com/documentation/instagram-platform/content-publishing` e referência `IG User Media`.

| Requisito Reels | Valor Meta | Nosso vídeo (Seedance 480p 9:16) | OK? |
|---|---|---|---|
| Container | MP4/MOV, moov atom no início | MP4 do provedor | ✅ (verificar moov) |
| Vídeo codec | H.264/HEVC, 4:2:0 | H.264 | ✅ |
| Áudio codec | AAC, ≤ 48 kHz, 1–2 canais | AAC (com áudio) | ✅ |
| Frame rate | 23–60 FPS | 24/30 FPS | ✅ |
| Aspect ratio | 0.01:1–10:1 (9:16 recomendado) | **9:16** | ✅ |
| Duração | 3 s – 15 min | **5–8 s** | ✅ |
| Tamanho arquivo | ≤ 300 MB | ~2–5 MB | ✅ |
| Largura máx. | 1920 px | 480p (≤ 854) | ✅ |

**Mínimo de 3 s**: vídeo tem que ter **no mínimo 3 segundos** — nosso range (5–8 s) atende com folga.

**Fluxo de publicação Reels** (mesmos endpoints já implementados no adapter):

```text
1. POST /v26.0/{ig-user-id}/media  → media_type=REELS & video_url=<presigned> & caption=<caption>
2. Poll GET /v26.0/{container_id}?fields=status_code  (a cada 60 s, até 5 min — IN_PROGRESS → FINISHED)
3. POST /v26.0/{ig-user-id}/media_publish  → creation_id=<container_id>
4. Poll permalink via GET /v26.0/{media_id}?fields=status_code,permalink
```

Diferenças para a imagem já publicada: `media_type=REELS` em vez de ausente (imagem), `video_url` em vez de `image_url`, e **timeout de polling maior** (vídeo demora mais para processar: minutos, não segundos).

**Publicly accessible**: a Meta faz cURL da `video_url` — continuamos usando MinIO + URL pré-assinada (24 h), como já funciona para imagens. Reels aceitam `video_url` diretamente no upload padrão (o `upload_type=resumable` só é necessário para arquivos grandes; nosso caso é ~3–5 MB).

## 2.4 Análise de custo — vídeo

| Item | Valor |
|---|---|
| Vídeos/mês | 2 (a cada 15 dias) |
| Custo/vídeo (Seedance 2.0 Mini, 5 s, 480p) | ~USD 0,08–0,11 |
| Custo mensal vídeo | **~USD 0,16–0,22** |
| Teto por vídeo (novo config) | `MaxVideoCostPerRequestUsd = 0.15` |
| Teto por execução (existe) | `MaxCostPerRunUsd = 0.06` → **precisa subir para ~0.20** quando a execução for de vídeo |
| Crédito atual OpenRouter | USD 5 — cobre ~2 anos de vídeo |

**Nota sobre `MaxCostPerRunUsd`**: como a execução de vídeo não gera imagem (e vice-versa), o teto por execução precisa ser **separado por modalidade**: `MaxCostPerRunUsd` (texto+imagem, 0.06) e `MaxCostPerVideoRunUsd` (texto+vídeo, ~0.20). Alternativa: um único teto de 0.20 para ambas — mais simples, mas perde precisão de budget na execução de imagem. Recomendação: **um único `MaxCostPerRunUsd = 0.20`** (mais simples; o teto real por execução continua sendo respeitado pela cadeia).

---

# 3. Análise de viabilidade — Leitura de comentários

## 3.1 A API permite? Sim, com ressalva de permissão

**Verificado na documentação oficial** (`Comment Moderation` + referência `IG Media Comments`):

- **Endpoint**: `GET /v26.0/{ig-media-id}/comments` — retorna até 50 comentários por consulta (top-level), com `id`, `text`, `timestamp` e `username` (via `from.username`).
- **Host**: `graph.instagram.com` — **o mesmo já usado pelo projeto**. Nenhum domínio novo.
- **Permissão necessária (Instagram Login)**: `instagram_business_basic` ✅ (já temos) + **`instagram_business_manage_comments`** ❌ (**o token atual não tem** — o token atual carrega `instagram_business_basic, instagram_business_content_publish, instagram_business_manage_messages, instagram_business_manage_insights, instagram_business_manage_comments` apenas se a conta foi reconectada com esse escopo; nosso token foi gerado com basic + content_publish).
- **Rate limit**: sem limite documentado específico para leitura de comentários; o limite geral de Graph API se aplica. Com 15 posts × 1 request cada, a cada 3 dias, o volume é trivial (~5 requests/execução).
- **Ordem**: v3.2+ retorna em ordem cronológica reversa (mais recentes primeiro) — ideal para "comentários desde a última execução".
- **Limitação**: não dá para filtrar por timestamp server-side; filtramos client-side por `timestamp > lastRun`.

### Alternativa (não usada no MVP): Webhooks `comments`

A documentação recomenda webhooks para evitar rate limit, mas exigiria um endpoint público de webhook na VPS + verificação de assinatura + validação do app na Meta. **Fora do escopo do MVP 2** — polling por API é suficiente para o volume (15 posts a cada 3 dias). Anotado como melhoria futura.

## 3.2 O problema da permissão (com fallback)

O token atual foi gerado com o painel "Casos de uso" e tem `instagram_business_basic` + `instagram_business_content_publish` (+ possivelmente `manage_messages`/`manage_insights`). **Sem `instagram_business_manage_comments`, `GET /{media-id}/comments` retorna erro OAuthException 10/190 ou code 3 "Application does not have permission".**

**Estratégia em 2 níveis:**

1. **Fallback automático (implementado)**: o `CommentSuggestionStep` captura o erro de permissão (403/codes 10/190/3 na resposta da Meta). Se ocorrer, loga `WARNING: comment permission missing — skipping comment suggestion step` e **o pipeline segue normalmente** (gera post por curiosidade, como hoje). Nenhuma execução falha por causa disso.
2. **Ativação manual (runbook)**: quando quiser ativar de verdade, seguir o passo-a-passo da seção 7.2 (regenerar token com a nova permissão no painel "Casos de uso" → `refresh_access_token` → atualizar `META_ACCESS_TOKEN` no VPS `.env`). A partir daí o step começa a funcionar sem deploy.

**Feature flag**: `AppConfiguration:Comments:Enabled` (default **false** até o token ser regenerado). Quando false, o step nem chama a API.

## 3.3 Fluxo de sugestão de tema

```text
PipelineOrchestrator.ExecuteAsync
  |
  ├─ [NOVO] CommentSuggestionStep (só quando Enabled=true e não é dia de vídeo)
  |     1. Buscar últimos 15 posts publicados (Publication.MetaMediaId)
  |     2. Para cada mediaId: GET /v26.0/{mediaId}/comments
  |        → coletar {id, text, timestamp, from.username}
  |     3. Filtrar comentários já processados (tabela CommentSuggestions, unique por CommentId)
  |     4. Chamar OpenRouter (texto, cadeia free como hoje) com todos os comentários novos:
  |        prompt classifica cada comentário:
  |          - é sugestão de tema para o perfil? (curiosidade incomum/fato)
  |          - se sim, extrai {theme, summary} normalizado
  |     5. Para cada sugestão candidata (no máximo 1 por execução):
  |        - valida igual à curiosidade normal:
  |          a. ContentHash duplicate (BR-004, 90 dias)
  |          b. Summary similarity ≥ 80% (BR-005)
  |          c. IA valida se é tema válido/adequado ao perfil (mesmo critério editorial BR-001)
  |     6. Se passou: marca o PipelineContext com SuggestionContext
  |        {Theme, Summary, AuthorUsername, CommentId, SourceCommentText}
  |        → TextGenerationStep usa esse tema em vez de sortear categoria/subcategoria
  |     7. Marca o CommentId como Processed (Accepted)
  |
  └─ Segue pipeline normal (Text → Image/Video → Publication)
```

**O que muda nos steps existentes:**

- **`TextGenerationStep`**: se `context.Suggestion` está preenchido, pula a seleção de categoria (o orchestrator não sorteia), passa o tema da sugestão como input da geração e injeta na legenda final: `...\n\nSuggested by @<username>`. O `Category`/`Subcategory` do post de sugestão: usar a categoria menos usada como fallback (o tema da sugestão não tem categoria estruturada no MVP — simplificação aceita).
- **`PublicationStep`**: imutável — publica a imagem como hoje.
- **Pós-publicação (novo use case)**: se a origem foi sugestão, `POST /v26.0/{comment-id}/replies` com `message="Thanks for the suggestion!"`. Falha de reply não falha o pipeline (log warning). **Nota de permissão**: responder comentário exige a mesma `instagram_business_manage_comments`.

## 3.4 Novos dados no banco

**Tabela `CommentSuggestions`** (auditoria e idempotência):

| Campo | Tipo | Nota |
|---|---|---|
| Id | long PK | |
| CommentId | string(120) | **unique index** — idempotência (comentário nunca processado 2×) |
| MediaId | string(120) | post Instagram onde o comentário está |
| AuthorUsername | string(120) | para crédito na legenda |
| CommentText | text | texto original |
| Classification | enum | `NotSuggestion`, `Rejected`, `Accepted` |
| RejectionReason | string(255) | null quando Accepted |
| ProcessedAt | DateTime(UTC) | |

**Migration**: nova migration EF Core (`AddCommentSuggestions`), auto-aplicada no startup (`ApplyMigrationsHostedService` já cuida disso — não mexer).

**Post**: coluna nullable `SourceCommentSuggestionId (long?)` FK → CommentSuggestions, para rastreabilidade do post gerado a partir de sugestão.

## 3.5 Análise de custo — comentários

| Item | Custo |
|---|---|
| Chamadas Meta (`/comments` 15 posts) | USD 0 (API gratuita) |
| Classificação IA (1 chamada de texto/execution, prompt ~1–3 k tokens com comentários + schema de resposta) | USD 0 (modelo `:free` — a cadeia de texto já existe) |
| Com fallback pago (cenário extremo, todos os free fora) | ≤ USD 0,01/execução (teto `MaxTextCostPerRequestUsd` já existente) |
| **Total mensal incremental** | **~USD 0** |

---

# 4. Análise arquitetural

## 4.1 O que muda em cada camada

### Domain (`OddOddities.Domain`)

| Item | Tipo | Descrição |
|---|---|---|
| `IVideoGenerationPort` | Port nova | `Task<VideoGenerationResult> GenerateVideoAsync(prompt, modelId, ct)` — resultado: bytes MP4, `CostUsd`, `DurationMs`. |
| `IMediaCommentPort` | Port nova | `Task<IReadOnlyList<MediaComment>> GetCommentsAsync(mediaId, ct)` + `Task ReplyToCommentAsync(commentId, message, ct)`. |
| `VideoGenerationResult` | Record | `ImageBytes` análogo: `VideoBytes`, `ModelId`, `CostUsd`, `DurationMs`. |
| `MediaComment` | Record | `CommentId`, `Text`, `Timestamp`, `AuthorUsername`. |
| `CommentSuggestion` | Entity | Tabela nova (seção 3.4). |
| `Post.SourceCommentSuggestionId` | Coluna nullable | Rastreabilidade. |
| `PostStatus` | Sem mudança | Reusa Generated → Validated → ImageProcessed → Published. (Para vídeo, `ImageProcessed` semanticamente vira "media processed" — decidir renomear ou reusar; **reusar** no MVP, sem migration de enum.) |
| `FailureStep` | +2 valores | `VideoGeneration`, `VideoStorage` (ou reusar `ImageGeneration`/`ImageStorage`; adicionar é barato pois é enum no banco). |
| `FailureStepMap` | Atualizar | Mapear `"VideoGeneration"` → novo enum. |
| `PipelineConstants` | +constantes | `MaxVideoModelAttempts`, `DefaultVideoDurationSeconds = 5`, `MaxVideoDurationSeconds = 8`, `MinVideoDurationSeconds = 3`, `VideoContainerPollingAttempts` (recomendação Meta: 1×/min até 5 min → 10 tentativas de 30 s cobre), `MaxCommentsPerMedia = 50`, `CommentLookbackPosts = 15`. |
| `AppConfiguration` | +seções | `VideoConfiguration { ModelId, DurationSeconds=5, Resolution="480p", AspectRatio="9:16", GenerateAudio=true, IntervalDays=15 }` e `CommentsConfiguration { Enabled=false, LookbackPosts=15 }`. + `ModelSelection.MaxVideoModelAttempts=3`, `MaxVideoCostPerRequestUsd=0.15`. |

### Application

| Item | Tipo | Descrição |
|---|---|---|
| `ModelSelectionService.GetVideoChainAsync()` | Método novo | Mesmo padrão de `GetImageChainAsync`: preferido da config → free → mais barato por segundo, dentro de `MaxVideoModelAttempts` e `MaxVideoCostPerRequestUsd`. |
| `OpenRouterModelCatalogAdapter.GetVideoModelsAsync()` | Método novo (Infra) | `GET /api/v1/videos/models`; mapeia `pricing_skus` → preço efetivo por segundo (menor SKU válido), `supported_durations`/`supported_aspect_ratios` → filtro 9:16 + duração 3–8 s. **Normalizar `video_tokens` SKUs para custo/segundo estimado** (fator configurável `VideoTokenSecondsFactor`, default ~1088 tokens/s). |
| `VideoGenerationStep : IPipelineStep` | Step novo | Espelho do `ImageGenerationStep`: cadeia → `POST /videos` → poll (30 s interval, timeout global ~15 min) → download MP4 → MinIO (`video/mp4`) → `Post.VideoObjectKey`. |
| `CommentSuggestionStep : IPipelineStep` | Step novo | Fluxo da seção 3.3. Primeiro step do pipeline. Falha de permissão → `StepResult` com flag nova `Skipped` (não marca Post Failed — ver 4.3). |
| `PipelineContext` | +campos | `SuggestionContext? Suggestion`, `VideoContext? Video` (ObjectKey, Bytes, DurationSeconds, CostUsd, ModelId). |
| `PipelineOrchestrator` | Modificar | **Não injeta categoria** quando `Suggestion` existe... (ver nota). Decision point: `bool isVideoRun = _scheduler.IsVideoRunToday()`. Passa para os steps via `PipelineContext.IsVideoRun`. Steps de imagem/vídeo fazem short-circuit: `if (!context.IsVideoRun) return StepResult.Skipped();` — **mantém a ordem declarativa e o foreach do orchestrator intactos**. |
| `ISchedulerPort` | +método | `bool IsVideoRunToday()` — implementa a regra "a cada N dias" (intervalo configurável, comparando com a data do último vídeo publicado no banco — ver 4.4). |
| `TextGenerationStep` | Modificar | Se `context.Suggestion != null`: usa o tema/summary da sugestão, injeta `Suggested by @user` na caption, registra `GenerationAttempt` normal, e valida hash/similaridade — se rejeitar a sugestão, o step **falha com código `SUGGESTION_REJECTED`** e a execução termina sem post (o comentário fica marcado `Rejected` — ver 4.2). |
| `PublicationStep` | Modificar | Escolhe `CreateMediaContainerAsync(image…)` vs `CreateReelsContainerAsync(video…)` conforme `context.IsVideoRun` (polling do container de vídeo mais longo). Pós-publicar, se `context.Suggestion != null` → `ReplyToCommentAsync` (fire-and-forget com log). |
| `IPipelineStep` / `StepResult` | +`Skipped` | Novo outcome para steps que não se aplicam à execução (imagem em run de vídeo e vice-versa). O orchestrator loga `outcome = Skipped` e segue. |

### Infrastructure

| Item | Tipo | Descrição |
|---|---|---|
| `OpenRouterVideoGenerationAdapter` | Adapter novo | `IVideoGenerationPort`. Fluxo assíncrono completo (submit/poll/download) com `OpenRouterModelException` em falhas. Timeout HttpClient: 20 min (job pode demorar minutos) — ou polling manual com CancellationToken de step. |
| `MetaInstagramPublishingAdapter` | Modificar | +`CreateReelsContainerAsync(videoUrl, caption, ct)` (`media_type=REELS&video_url=…`), +`GetCommentsAsync`, +`ReplyToCommentAsync`. Reusa `EnsureSuccessAsync` (que já embute o body de erro da Meta na `HttpRequestException` — o CommentSuggestionStep inspeciona a mensagem por codes 10/190/3 para o fallback de permissão). |
| `PostgresPostRepository` / novo `PostgresCommentSuggestionRepository` | Repos | Query: últimos 15 posts publicados com `MetaMediaId` (join Publications). `CommentSuggestions` CRUD + `ExistsByCommentId`. |
| Migration `AddCommentSuggestions` | EF Core | Tabela nova + `Post.SourceCommentSuggestionId`. Auto-aplicada no startup. |
| MinIO | Sem mudança | `PutObjectAsync` já aceita content type genérico → `video/mp4`. |

### Worker

- `appsettings.json`: seções `Video` e `Comments` novas (defaults); `appsettings.Development.json` idem; produção via env vars (`VIDEO_MODEL_ID`, `VIDEO_DURATION_SECONDS`, `VIDEO_INTERVAL_DAYS`, `COMMENTS_ENABLED`, `COMMENTS_LOOKBACK_POSTS`).
- `Worker.cs`: sem mudança de loop; o agendamento de vídeo é resolvido dentro do pipeline via `IsVideoRunToday()`.

## 4.2 Semântica de falha da sugestão (importante)

- **Sugestão rejeitada pela validação editorial (hash/similaridade/IA)**: a execução **não publica post** — diferente do fluxo normal, que re-tenta com nova curiosidade. Motivo: o tema foi escolhido por um usuário; re-tentar com outro tema quebraria a expectativa de "a sugestão virou post". `CommentSuggestion` marcada `Rejected` com o motivo; log warning; pipeline termina com sucesso (não é falha de sistema).
- **Implementação**: `CommentSuggestionStep` valida o tema da sugestão **antes** de marcar o contexto (hash + similaridade + classificação editorial em 1 chamada de texto). Se rejeitada → `StepResult.Skipped` com log explicativo e o pipeline segue o fluxo normal (gera curiosidade por categoria). Assim nunca ficamos sem post no dia.

**Correção do fluxo 3.3**: o passo 5/6 vira — sugestão válida → `context.Suggestion` preenchido e `TextGenerationStep` usa o tema; sugestão inválida → `context.Suggestion = null` e pipeline segue normal. **A execução sempre produz um post** (sugestão OU curiosidade), mantendo a cadência de 3/semana.

## 4.3 Orquestração — decisão de desenho

Requisito do dono: "ao acionar cada execução deve identificar se é uma execução que irá gerar vídeo ou imagem. Se for imagem, executa o ImageStep e pula o VideoStep, se for vídeo faz o contrário."

Avaliadas 3 opções:

1. **Dois pipelines/threads separados** — rejeitada: duplica orchestrator, scheduler e lock; maior superfície de bug; viola "single pipeline execution at a time" (SemaphoreSlim).
2. **Steps Skip-able (escolhida)** — `IsVideoRun` resolvido 1× no orchestrator (via `ISchedulerPort.IsVideoRunToday()`), colocado no `PipelineContext`. `ImageGenerationStep` e `VideoGenerationStep` short-circuit com `StepResult.Skipped()`. `PublicationStep` decide imagem vs reels pelo contexto. Ordem declarativa preservada, foreach intacto, 1 lock só.
3. **Branching no orchestrator** — rejeitada: lógica debranching dentro do orchestrator conflita com o desenho atual (steps independentes e sequenciais).

**Decisão**: opção 2 + `StepResult.Skipped()` como novo outcome (`IsSuccess = true`, loga `outcome=Skipped`, não falha o pipeline).

## 4.4 Regra "a cada 15 dias"

`IsVideoRunToday()`: busca no banco a `PublishedAt` do último post com vídeo (`Post.VideoObjectKey != null`); se `hoje - lastVideoPublishedAt >= IntervalDays (15)` **ou nunca houve vídeo**, é dia de vídeo. Fallback se query falhar: false (comportamento atual, post de imagem).

Edge cases:
- Dia de vídeo + falha na geração do vídeo → post falha; próxima execução (3 dias depois) será vídeo de novo (intervalo ainda não completado desde o último publicado... **cuidado**: se o último vídeo foi há 15+ dias e a execução falha, a próxima execução em 3 dias também será vídeo — comportamento correto, tenta de novo até conseguir).
- Primeira execução após deploy: nunca houve vídeo → `IsVideoRunToday() = true` → publica vídeo no primeiro dia. Aceitável (valida o fluxo cedo). Alternativa: flag `Video.SeedDelayDays` — desnecessária no MVP.

## 4.5 Budget de custo por modalidade

`PipelineContext.AccumulatedCostUsd` continua único (texto + imagem OU texto + vídeo na mesma execução — nunca ambos). Teto `MaxCostPerRunUsd`: subir de 0.06 → **0.20** no appsettings (cobre 1 vídeo 0.11 + texto ~0 + margem para 1 retry de modelo). Texto continua ~0 (free). Estimativa de vídeo pré-call: `preço/segundo (SKU) × DurationSeconds`, com fator de conversão para SKUs `video_tokens`.

## 4.6 Conformidade com as regras de negócio existentes

| Regra | Impacto |
|---|---|
| BR-001 (conteúdo factual) | IA valida sugestão com o mesmo critério editorial. |
| BR-002 (800 chars) | Legenda de vídeo = mesma legenda da curiosidade (não muda). |
| BR-004/BR-005 (duplicata/similaridade) | Aplicadas à sugestão antes de aceitar (seção 4.2). |
| BR-006 (3 tentativas) | Vídeo: cadeia de modelos com fallback; rejeição editorial não se aplica a vídeo (sem validação de conteúdo textual além da prompt). |
| BR-008 (1080×1080 imagem) | Inalterado; vídeo é fluxo separado (480p 9:16). |
| BR-009 (quota MinIO) | Vídeos de 5 MB × 2/mês = 120 MB/ano — desprezível vs 20 GB. |
| BR-011 (toda publicação grava Publication) | Idem para reels (mesma tabela). |
| BR-012 (uma imagem por execução) | Texto da regra vira "uma mídia por execução" (imagem OU vídeo) — atualizar descrição na doc. |
| BR-014 (nada é excluído) | Vídeos também permanecem no MinIO. |

---

# 5. Custos consolidados (MVP 2)

| Item | Mensal | Observação |
|---|---|---|
| VPS + domínio + infra | USD 6,00 | Inalterado |
| Texto (curiosidades + classificação de comentários) | USD 0 | Modelos free |
| Imagens | USD 0,0004 | Inalterado |
| **Vídeo (novo)** | **~USD 0,16–0,22** | 2× Seedance 2.0 Mini 5 s 480p (~0,08–0,11/vídeo) |
| Meta API | USD 0 | Gratuita |
| **Total** | **~USD 6,20/mês** | +USD 0,20 vs MVP 1 |

Cenário pessimista (todos os vídeos no fallback mais caro, USD 0,15 teto): USD 0,30/mês. Crédito atual (USD 5) cobre > 2 anos.

---

# 6. Riscos e mitigações

| ID | Risco | Impacto | Prob. | Mitigação |
|---|---|---|---|---|
| R14 | `instagram_business_manage_comments` ausente no token → step de comentários inoperante | Médio (feature off) | **Alta** (quase certa) | Fallback: step pulado com warning + feature flag `Comments:Enabled=false` default. Runbook de regeneração de token (7.2). |
| R15 | Preço por video-token do Seedance sem denominador documentado → estimativa errada de custo/budget | Médio | Média | Primeira execução: comparar `usage.cost` real vs estimado; ajustar `VideoTokenSecondsFactor`; budget hard-stop continua ativo (falha antes de estourar 0.20). |
| R16 | Nenhum modelo de vídeo gratuito; preços podem mudar no catálogo | Baixo | Média | Cadeia dinâmica re-ordena a cada execução (ADR-008); teto por request rejeita modelos caros automaticamente. |
| R17 | Vídeo não conforme Reels (moov atom, codec, duração < 3 s) → container ERROR na Meta | Médio | Baixa | Seedance gera MP4 H.264 AAC padrão; 480p 9:16 ≥ 5 s; polling do container com timeout maior; se ERROR → `FailureStep=VideoGeneration`/`InstagramApi` com reason no Post. |
| R18 | Geração de vídeo demora minutos → step pode estourar timeouts do pipeline | Médio | Média | Timeout dedicado (~20 min) no VideoGenerationStep; polling com intervalo 30 s; `HttpClient.Timeout` compatível. Execução do vídeo só 2×/mês — lentidão não afeta as demais execuções. |
| R19 | Commentário de spam/troll vira sugestão publicada | Médio | Média | Classificação IA + validação editorial (hash, similaridade) + máximo 1 sugestão por execução + flag global para desligar. |
| R20 | Rate limit da Meta ao ler 15 posts × comments | Baixo | Baixa | ~15 GET/execução, 10×/mês — trivial. Webhooks como melhoria futura. |
| R21 | `PostStatus.ImageProcessed` usado para vídeo (semântica) | Baixo | Baixa | Reusar enum no MVP (sem migration de enum); documentar. Renomear em refactor futuro se incomodar. |
| R22 | Reply a comentário falha depois do post publicado | Baixo | Média | Fire-and-forget com log; o crédito `Suggested by @user` já está na legenda (não depende do reply). |

---

# 7. Plano de implementação

## 7.1 Ordem de trabalho (estimativas para 1 dev)

### Fase 0 — Infra de decisão (0,5 dia)

1. `AppConfiguration`: seções `Video`, `Comments`; `ModelSelection.MaxVideoModelAttempts`, `MaxVideoCostPerRequestUsd`, `MaxCostPerRunUsd` → 0.20.
2. `ISchedulerPort.IsVideoRunToday()` + implementação (query último vídeo publicado + `IntervalDays`).
3. `StepResult.Skipped()` + suporte no `PipelineOrchestrator` (log `outcome=Skipped`).
4. `PipelineContext.IsVideoRun`, `Suggestion`, `Video` contextos.
5. Unit tests: decisão de run (vídeo vs imagem), Skipped outcome.

### Fase 1 — Vídeo (2–3 dias)

1. Domain: `IVideoGenerationPort`, `VideoGenerationResult`, `FailureStep.VideoGeneration/VideoStorage`, constantes de vídeo.
2. Infra: `OpenRouterModelCatalogAdapter.GetVideoModelsAsync()` (`/api/v1/videos/models`, normalização de `pricing_skus`, filtro 9:16 + duração ≤ 8 s, ordenação free → mais barato).
3. Application: `ModelSelectionService.GetVideoChainAsync()` (+ unit tests de ordenação/tetos).
4. Infra: `OpenRouterVideoGenerationAdapter` (submit → poll → download, `OpenRouterModelException`).
5. Application: `VideoGenerationStep` (espelho do ImageGenerationStep: cadeia, budget, `GenerationAttempt`, MinIO `video/mp4`, `Post.VideoObjectKey`).
6. Infra: `MetaInstagramPublishingAdapter.CreateReelsContainerAsync` (`media_type=REELS&video_url`) + `PublicationStep` branch imagem/reels + polling de container maior para vídeo.
7. Migration: `Post.VideoObjectKey/VideoBytes/VideoDurationSeconds` (nullable).
8. Unit tests: chain de vídeo, fallback, budget; step com NSubstitute.
9. **Validação manual end-to-end**: 1 vídeo real publicado como Reels (validar custo real vs estimado — ajustar fator).

### Fase 2 — Comentários (2–3 dias)

1. Domain: `IMediaCommentPort`, `MediaComment`, entity `CommentSuggestion`, `FailureStep.CommentModeration` (se necessário), constantes (`CommentLookbackPosts=15`, `MaxCommentsPerMedia=50`).
2. Migration `AddCommentSuggestions` + `Post.SourceCommentSuggestionId` + repos (`PostgresCommentSuggestionRepository`).
3. Infra: `MetaInstagramPublishingAdapter.GetCommentsAsync` + `ReplyToCommentAsync`; deteção de erro de permissão (codes 10/190/3).
4. Application: `CommentSuggestionStep` (primeiro step): busca → filtro de idempotência → classificação IA (1 chamada) → validação editorial → `context.Suggestion` ou skip.
5. `TextGenerationStep` alterado (tema da sugestão + `Suggested by @user` na caption + `SourceCommentSuggestionId` no Post).
6. `PublicationStep` alterado (reply pós-publicação, fire-and-forget).
7. DI: registrar ports/adapters/repos; registrar `CommentSuggestionStep` **antes** do `TextGenerationStep`.
8. Unit tests: classificação (sugestão/não-sugestão/rejeitada), idempotência por CommentId, fallback de permissão, integração com TextGeneration.
9. Validação manual: com flag off (deve pular) e — após regenerar token — flag on.

### Fase 3 — Documentação e deploy (0,5–1 dia)

1. Atualizar `docs/architecture.md` (fluxo, portas, adapters, custos, regra BR-012) e `docs/openrouter.md` (seção vídeo), `docs/instagram-api.md` (Reels + comments).
2. ADR-009: "Pipeline de vídeo alternativo com Skipped steps" (captura a decisão 4.3).
3. ADR-010: "Sugestões de tema via comentários com fallback de permissão" (decisões 3.2/4.2).
4. `.env.example` + GitHub Variables (`VIDEO_*`, `COMMENTS_*`).
5. Deploy + validação em produção (primeiro vídeo real, primeiro ciclo de comentários).

**Total estimado: 5–7 dias de trabalho.**

## 7.2 Runbook — ativar permissão de comentários (quando o token for regenerado)

1. Painel Meta → seu app → **Casos de uso** → "Gerenciar mensagens e conteúdo no Instagram" → **Personalizar**.
2. Em "Permissões", adicionar `instagram_business_manage_comments` (se o fluxo do painel não listar, gerar o token de novo pelo passo "Generate access token" — o painel novo concede o conjunto completo basic + content_publish + manage_comments).
3. `GET https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token=<novo>` → confira no `permissions` da resposta que `instagram_business_manage_comments` está presente.
4. Atualizar `META_ACCESS_TOKEN` no `.env` da VPS + redeploy (`docker compose up -d`).
5. Setar `COMMENTS_ENABLED=true` (env var) + redeploy.
6. Validar: execução manual do pipeline; log esperado: `CommentSuggestionStep: fetched N comments across M medias`.

## 7.3 Critérios de aceite do MVP 2

- [ ] Execução decide vídeo vs imagem automaticamente (intervalo de 15 dias) e publica exatamente uma mídia por execução.
- [ ] Vídeo: ≤ 8 s, 9:16, publicado como Reels via `media_type=REELS`; cadeia de modelos free→barato com fallback e budget; custo real registrado em `GenerationAttempt.CostUsd`.
- [ ] Vídeo publicado aparece na aba Reels com a legenda padrão (curiosidade + Source).
- [ ] Com `Comments:Enabled=false`: `CommentSuggestionStep` loga skip e pipeline idêntico ao MVP 1.
- [ ] Com flag on e permissão concedida: comentários dos últimos 15 posts classificados; sugestão válida vira post com `Suggested by @user` na legenda + reply no comentário; sugestão inválida não impede o post do dia; comentário nunca processado 2× (unique CommentId).
- [ ] Sem permissão: step de comentários pulado com warning, pipeline segue (fallback).
- [ ] Todas as execuções (normal e vídeo) respeitam `SemaphoreSlim(1,1)` e budget por modalidade (`MaxCostPerRunUsd` para texto+imagem; `MaxCostPerVideoRunUsd` para texto+vídeo — seção 8.2).
- [ ] `dotnet build` + `dotnet test` verdes; migrations aplicam no startup sem passo manual.

---

# 8. Revisão (2026-09-29)

Revisão do planejamento contra o código atual (`PipelineOrchestrator`, `StepResult`, `AppConfiguration`, `FailureStepMap`, `docker-compose.yml`, `docs/prd.md`, ADR-008) e a documentação oficial da OpenRouter video generation. Os RFs da seção 9 são a **fonte de verdade para implementação** (seções 4 e 7 acima continuam válidas como design; onde divergirem, vale o RF).

## 8.1 Correções aplicadas ao design original

| # | Problema no original | Correção nos RFs |
|---|---|---|
| 1 | Budget contraditório: 2.4 discutia `MaxCostPerVideoRunUsd`, 4.5 recomendava um único teto de 0.20. Subir `MaxCostPerRunUsd` para 0.20 mudaria o comportamento das cadeias de texto/imagem **existentes** (elas comparam contra esse teto). | **Decisão do dono: teto separado por modalidade.** Novo `ModelSelection.MaxCostPerVideoRunUsd = 0.20` usado quando `context.IsVideoRun = true`; `MaxCostPerRunUsd` permanece 0.06 para texto+imagem. Steps recebem o teto do contexto (RF-15). |
| 2 | Env vars propostas (`VIDEO_MODEL_ID`, `COMMENTS_ENABLED`) não seguem o padrão do projeto (`AppConfiguration__Secao__Propriedade`, ver `docker-compose.yml`). Deploy em produção não receberia config. | Env vars no formato `AppConfiguration__Video__*` e `AppConfiguration__Comments__*` (RF-18). |
| 3 | `IsVideoRunToday()` (Fase 0) consulta `Post.VideoObjectKey`, coluna criada só na Fase 1 — Fase 0 não compila/testa isolada. | Migração de colunas de vídeo **antes** da decisão de modalidade (RF-14 colunas → RF-15 scheduler; ordem dos RFs já resolve). |
| 4 | `CommentSuggestionStep` como "primeiro step" precisa de `PostId` que só existe depois de `TextGenerationStep` — e a 3.3 validava hash/similaridade num Post ainda inexistente. | Comentários são lidos/classificados ANTES do texto (`CommentSuggestionStep` primeiro), mas **validação editorial (hash/similaridade) só acontece DENTRO do TextGenerationStep**, depois que o texto do tema sugerido é gerado — aí sim existe o que hashar. Post criado apenas após validação, como hoje. Sugestão rejeitada → `context.Suggestion = null` e re-geração pelo fluxo normal de categoria (BR-006), sem post perdido. |
| 5 | `MaxCommentsPerMedia = 50` tratado como constante de domínio, mas é page size da Meta; não deixa claro se pagina. | Port retorna página completa; implementação usa `limit=50` + paginação por `after` até esgotar ou atingir `Comments.LookbackPosts`; 50 documentado como limite aceito por request (RF-20). |
| 6 | `PublicationStep` chamaria `ReplyToCommentAsync` sem ter as dependências (hoje não recebe `IMediaCommentPort` nem a flag de comentários). | RF-21 explicita as novas dependências do `PublicationStep`. |
| 7 | Tabela 2.2 mistura SKUs por video-token e por segundo; catálogo real de `/videos/models` usa SKUs `per-video-second[-<res>]`. | Normalização única: custo efetivo/segundo = menor SKU `per-video-second*` do modelo; budget estimado = `custo/s × DurationSeconds` (RF-16). |
| 8 | `StepResult.Skipped()` com `IsSuccess = true` seria logado como `Success` pelo orchestrator atual — critério de aceite "loga skip" não seria verificável. | `StepResult` ganha outcome explícito; orchestrator loga `outcome=Skipped` sem marcar Post como Failed (RF-13). |
| 9 | `docs/instagram-api.md` mostra exemplo de resposta com `instagram_business_manage_comments` no token — conflita com a premissa "o token não a tem". | Runbook 7.2 inalterado; RF-22 exige log explícito do erro de permissão (codes 10/190/3) para diagnóstico, e a seção 3.2 já defaulta `Comments:Enabled=false`. |

## 8.2 Decisões finais de design (resumo para implementação)

1. **Budget**: `MaxCostPerRunUsd` (0.06, texto+imagem) e `MaxCostPerVideoRunUsd` (0.20, texto+vídeo). `ModelCostEstimator.FitsBudget` recebe o teto conforme `PipelineContext.IsVideoRun`. Valor efetivo resolvido uma vez no orchestrator e exposto em `PipelineContext.CostCeilingUsd` (evita cada step decidir).
2. **Modalidade**: `IsVideoRunToday()` resolvido 1× no orchestrator via `ISchedulerPort.IsVideoRunToday()`; steps de mídia fazem short-circuit com `StepResult.Skipped()`.
3. **Ordem do pipeline**: `CommentSuggestionStep` → `TextGenerationStep` → `ImageGenerationStep` → `VideoGenerationStep` → `PublicationStep` (steps de mídia skipam conforme a modalidade).
4. **Sugestão rejeitada**: validação editorial dentro do `TextGenerationStep`; se o texto do tema sugerido falhar hash/similaridade, marca `CommentSuggestion.Rejected`, limpa `context.Suggestion` e re-tenta com o fluxo normal de categoria (mesmo budget de tentativas BR-006). **A execução sempre produz post** (exceto falha de sistema).
5. **PostStatus**: reusar `ImageProcessed` para vídeo (sem migration de enum — decisão 4.4 do original, mantida).

---

# 9. Requisitos Funcionais — MVP 2

> **Atenção ao `/implement-all-prd`:** este documento (`mvp2-planning.md`) não é lido pelo command — ele extrai RFs apenas de `docs/prd.md`. Após implementar/copiar esta seção para o `prd.md` (ou ajustar o command), os RFs abaixo são implementados **nesta ordem**; cada um compila e testa isoladamente.

## [x] RF-13: Outcome Skipped no pipeline

**User Story:** Como dono, quero que steps que não se aplicam à execução sejam pulados sem falhar o pipeline, para que a decisão vídeo/imagem conviva com o foreach sequencial atual.

**Criterios de aceitacao:**

1. `StepResult` ganha outcome `Skipped` (estaticamente: `StepResult.Skipped()`), com `IsSuccess = true` e `FailureStep = null`.
2. `PipelineOrchestrator` loga `outcome = Skipped` (distinguido de `Success`) via `ILogCorrelationPort` e continua para o próximo step.
3. Outcome `Skipped` NUNCA marca o Post como `Failed`.
4. Steps existentes (`TextGenerationStep`, `ImageGenerationStep`, `PublicationStep`) não mudam de comportamento neste RF.
5. Unit tests: orchestrator pula step skipped e segue; step skipped não marca Post Failed.

**Arquivos-alvo:** `src/OddOddities.Application/Pipeline/IPipelineStep.cs`, `src/OddOddities.Application/Pipeline/PipelineOrchestrator.cs`, `tests/OddOddities.UnitTests/`.

**Dependencias:** nenhuma.

---

## [x] RF-14: Colunas e constantes de vídeo no Post

**User Story:** Como dono, quero persistir os metadados do vídeo gerado no Post, para auditoria e para a regra de intervalo do RF-15.

**Criterios de aceitacao:**

1. Migration EF Core adiciona colunas nullable em `Posts`: `VideoObjectKey VARCHAR(255)`, `VideoBytes BIGINT`, `VideoDurationSeconds INT`.
2. Entity `Post` ganha as mesmas propriedades nullable; `PostConfiguration` atualizada.
3. `PipelineConstants` ganha `DefaultVideoDurationSeconds = 5`, `MaxVideoDurationSeconds = 8`, `MinVideoDurationSeconds = 3`, `MaxVideoJobPollingAttempts = 30`, `VideoJobPollingIntervalSeconds = 30` (cobre ~15 min de job), `MaxReelsContainerPollingAttempts = 10`, `ReelsContainerPollingIntervalSeconds = 30` (cobre ~5 min de processamento Meta).
4. `FailureStep` ganha `VideoGeneration = 6`, `VideoStorage = 7`, `CommentModeration = 8` (append-only; sem reordenar os existentes).
5. `FailureStepMap` mapeia `"VideoGenerationStep"` → `VideoGeneration` e `"VideoStorage"` → `VideoStorage`.
6. Migration aplica automaticamente no startup (comportamento existente de `ApplyMigrationsHostedService` — não alterar).
7. `dotnet build` e `dotnet test` verdes após o RF.

**Arquivos-alvo:** `src/OddOddities.Domain/Entities/Post.cs`, `src/OddOddities.Domain/Enums/PipelineEnums.cs`, `src/OddOddities.Domain/Constants/PipelineConstants.cs`, `src/OddOddities.Application/Pipeline/FailureStepMap.cs`, `src/OddOddities.Infrastructure/Data/Configurations/EntityConfigurations.cs`, nova migration em `src/OddOddities.Infrastructure/Migrations/`.

**Dependencias:** nenhuma (pode rodar em paralelo ao RF-13; deve existir antes do RF-16).

---

## [x] RF-15: Decisão de modalidade por execução (imagem vs vídeo)

**User Story:** Como dono, quero que cada execução decida uma única vez se gera vídeo ou imagem, para que a cadência "1 vídeo a cada 15 dias" seja automática.

**Criterios de aceitacao:**

1. `ISchedulerPort` ganha `bool IsVideoRunToday()`.
2. Regra: busca `PublishedAt` do último Post publicado com `VideoObjectKey != null`; se `hoje - lastVideoPublishedAt >= AppConfiguration.Video.IntervalDays` OU nunca houve vídeo → true. Fallback: query falha → false (comportamento atual). Repositório expõe o dado via novo método em `IPostRepository` (ex.: `GetLatestVideoPublishedAtAsync`).
3. `PipelineOrchestrator` chama `IsVideoRunToday()` 1× e grava `PipelineContext.IsVideoRun` (bool) e `PipelineContext.CostCeilingUsd` (= `MaxCostPerVideoRunUsd` se vídeo, senão `MaxCostPerRunUsd`).
4. `PipelineContext` ganha `IsVideoRun`, `CostCeilingUsd` e sub-contextos nullable `VideoContext? Video` e `SuggestionContext? Suggestion` (registros vazios definidos neste RF; preenchidos nos RFs 17/19/20).
5. `AppConfiguration` ganha `VideoConfiguration` (propriedades `ModelId`, `DurationSeconds = 5`, `Resolution = "480p"`, `AspectRatio = "9:16"`, `GenerateAudio = true`, `IntervalDays = 15`) e `ModelSelectionConfiguration.MaxVideoModelAttempts = 3`, `MaxVideoCostPerRequestUsd = 0.15`, `MaxCostPerVideoRunUsd = 0.20` (todos com defaults em código; seções opcionais em appsettings).
6. `TextGenerationStep` e `ImageGenerationStep` passam a usar `context.CostCeilingUsd` no lugar de `settings.MaxCostPerRunUsd` (comportamento idêntico hoje: 0.06).
7. Unit tests: `IsVideoRunToday` (nunca houve vídeo → true; dentro do intervalo → false; intervalo completado → true; query falha → false); Ceiling correto por modalidade.

**Arquivos-alvo:** `src/OddOddities.Domain/Interfaces/ISchedulerPort.cs`, `src/OddOddities.Domain/Interfaces/IPostRepository.cs`, `src/OddOddities.Domain/ValueObjects/AppConfiguration.cs`, `src/OddOddities.Application/Pipeline/PipelineContext.cs`, `src/OddOddities.Application/Pipeline/PipelineOrchestrator.cs`, `src/OddOddities.Application/UseCases/ScheduleService.cs`, `src/OddOddities.Infrastructure/Adapters/PostgresPostRepository.cs`, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-13, RF-14.

---

## [x] RF-16: Cadeia de modelos de vídeo (catálogo dinâmico)

**User Story:** Como dono, quero a cadeia de modelos de vídeo seguindo o padrão ADR-008, para pagar o mínimo por segundo dentro dos tetos.

**Criterios de aceitacao:**

1. `IModelCatalogPort` ganha `GetVideoModelsAsync(ct)` retornando descritores de vídeo (id, menor SKU `per-video-second*` normalizado como custo efetivo por segundo, `supported_durations`, `supported_aspect_ratios`).
2. Infra busca `GET {BaseUrl}/videos/models`; modelo sem SKU `per-video-second*` não é elegível.
3. `IModelSelectionService` ganha `GetVideoChainAsync(ct)`: preferido `AppConfiguration.Video.ModelId` primeiro (mesmo fora do catálogo), depois free (se existir), depois mais barato por segundo; filtro por `supported_aspect_ratios` contendo `Video.AspectRatio` e por `supported_durations` contendo `Video.DurationSeconds`; máx `MaxVideoModelAttempts`; custo estimado `custo/segundo × DurationSeconds <= MaxVideoCostPerRequestUsd`.
4. `ModelDescriptor` estendido ou novo record `VideoModelDescriptor` — seguir o padrão existente.
5. Catalog load com cache por execução (mesmo padrão dos catálogos de texto/imagem) e fallback para o modelo configurado quando o catálogo falha.
6. Unit tests: ordenação free→barato, filtro 9:16 + duração, teto por request, preferred primeiro, catálogo indisponível → só o modelo configurado.

**Arquivos-alvo:** `src/OddOddities.Domain/Interfaces/IModelCatalogPort.cs`, `src/OddOddities.Application/Services/IModelSelectionService.cs`, `src/OddOddities.Application/Services/ModelSelectionService.cs`, `src/OddOddities.Infrastructure/Adapters/OpenRouterModelCatalogAdapter.cs`, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-15 (config de vídeo).

---

## [x] RF-17: Geração de vídeo via OpenRouter (assíncrono)

**User Story:** Como dono, quero gerar MP4s verticais curtos via API assíncrona da OpenRouter, para publicar Reels sem intervenção.

**Criterios de aceitacao:**

1. Port `IVideoGenerationPort` no Domain: `Task<VideoGenerationResult> GenerateVideoAsync(string prompt, string modelId, CancellationToken ct)`; `VideoGenerationResult` record: `VideoBytes`, `ModelId`, `CostUsd`, `DurationSeconds`.
2. Adapter Infra `OpenRouterVideoGenerationAdapter`: `POST {BaseUrl}/videos` (202, `id`/`polling_url`) → poll `GET {BaseUrl}/videos/{jobId}` a cada `VideoJobPollingIntervalSeconds` até `completed`/`failed` (máx `MaxVideoJobPollingAttempts`) → download `GET {BaseUrl}/videos/{jobId}/content?index=0` **com `Authorization: Bearer`** (URLs `unsigned_urls` não são pré-assinadas). `usage.cost` real vira `CostUsd`.
3. Falhas de submit/poll/download viram `OpenRouterModelException` (mesma semântica do adapter de imagem); timeout global do job respeita `MaxVideoJobPollingAttempts × intervalo`; cancelamento propaga.
4. `VideoGenerationStep : IPipelineStep` (StepName `VideoGeneration`): espelho do `ImageGenerationStep` — cadeia via `GetVideoChainAsync`, budget estimado `custo/segundo × DurationSeconds` contra `context.CostCeilingUsd`, record em `GenerationAttempt` por tentativa, prompt derivado do tema/texto (mesma diretriz surrealista da imagem), MinIO upload `video/mp4` com quota BR-009, Post atualizado com `VideoObjectKey`/`VideoBytes`/`VideoDurationSeconds` e `Status = ImageProcessed` (reuso semântico), `context.Video` preenchido.
5. Short-circuit: se `!context.IsVideoRun` → `StepResult.Skipped()` com log.
6. DI: port + adapter + step registrados (`ApplicationServiceCollectionExtensions` / `InfrastructureServiceCollectionExtensions`); step adicionado ao pipeline DEPOIS de `ImageGenerationStep`.
7. Unit tests: skip fora de run de vídeo; budget exceeded; falha de todos os modelos → `FailureStep.VideoGeneration`; sucesso grava `GenerationAttempt` + Post + `context.Video`.

**Arquivos-alvo:** `src/OddOddities.Domain/Interfaces/IVideoGenerationPort.cs` (novo), `src/OddOddities.Domain/ValueObjects/` (novo record resultado), `src/OddOddities.Infrastructure/Adapters/OpenRouterVideoGenerationAdapter.cs` (novo), `src/OddOddities.Application/Steps/VideoGenerationStep.cs` (novo), DI de Application e Infrastructure, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-13, RF-14, RF-15, RF-16.

---

## [x] RF-18: Publicação de Reels e configuração/deploy

**User Story:** Como dono, quero que o vídeo seja publicado como Reels pela mesma Meta Graph API e que a nova config chegue por env vars no padrão do projeto, para o deploy de produção funcionar sem mudanças manuais.

**Criterios de aceitacao:**

1. `IInstagramPublishingPort` ganha `CreateReelsContainerAsync(string videoUrl, string caption, CancellationToken ct)` (`media_type=REELS&video_url=...`).
2. `PublicationStep`: se `context.IsVideoRun`, gera presigned URL do `context.Video.ObjectKey` e usa `CreateReelsContainerAsync`; polling do container usa `MaxReelsContainerPollingAttempts`/intervalo próprios de vídeo; senão fluxo de imagem atual.
3. Timeout do step: vídeo usa `MaxReelsContainerPollingAttempts × ReelsContainerPollingIntervalSeconds + margem` (não o `PublicationStepTimeoutSeconds` de 240 s de imagem).
4. Short-circuit: run de vídeo sem `context.Video` preenchido → `StepResult.Failure(VideoGeneration, ...)` (não deve acontecer; defensivo).
5. `appsettings.json` e `appsettings.Development.json` ganham seções `AppConfiguration:Video` (defaults) e `ModelSelection` atualizado (`MaxVideoModelAttempts`, `MaxVideoCostPerRequestUsd`, `MaxCostPerVideoRunUsd`).
6. `docker-compose.yml` e `.env.example`: `AppConfiguration__Video__ModelId=${VIDEO_MODEL_ID}`, `__DurationSeconds=${VIDEO_DURATION_SECONDS:-5}`, `__Resolution=${VIDEO_RESOLUTION:-480p}`, `__AspectRatio=${VIDEO_ASPECT_RATIO:-9:16}`, `__GenerateAudio=${VIDEO_GENERATE_AUDIO:-true}`, `__IntervalDays=${VIDEO_INTERVAL_DAYS:-15}`, e `AppConfiguration__ModelSelection__MaxCostPerVideoRunUsd=${MAX_VIDEO_COST_PER_RUN_USD:-0.20}` — variáveis simples `VIDEO_*` só como aliases no `.env.example`.
7. Unit tests: branch imagem vs reels no PublicationStep (NSubstitute).

**Arquivos-alvo:** `src/OddOddities.Domain/Interfaces/IInstagramPublishingPort.cs`, `src/OddOddities.Application/Steps/PublicationStep.cs`, `src/OddOddities.Infrastructure/Adapters/MetaInstagramPublishingAdapter.cs`, `src/OddOddities.Worker/appsettings.json`, `src/OddOddities.Worker/appsettings.Development.json`, `docker-compose.yml`, `.env.example`, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-17.

---

## [x] RF-19: Cadeia de comentários — port, entidade e persistência

**User Story:** Como dono, quero ler e registrar comentários com idempotência por CommentId, para nunca processar o mesmo comentário duas vezes.

**Criterios de aceitacao:**

1. Port `IMediaCommentPort` no Domain: `Task<IReadOnlyList<MediaComment>> GetCommentsAsync(string mediaId, CancellationToken ct)` e `Task ReplyToCommentAsync(string commentId, string message, CancellationToken ct)`; record `MediaComment(CommentId, Text, Timestamp, AuthorUsername)`.
2. Entity `CommentSuggestion` com campos da seção 3.4 (Id, CommentId unique index, MediaId, AuthorUsername, CommentText, Classification enum `NotSuggestion/Rejected/Accepted`, RejectionReason(255), ProcessedAt) + repositório no Domain (`ICommentSuggestionRepository`: `ExistsByCommentIdAsync`, `CreateAsync`, `UpdateAsync`).
3. `Post.SourceCommentSuggestionId (long?)` FK → CommentSuggestions.
4. Migration `AddCommentSuggestions` (tabela + coluna no Post + índice unique em CommentId), auto-aplicada no startup.
5. Adapter Infra: `MetaInstagramPublishingAdapter` implementa `IMediaCommentPort`: `GET /v26.0/{mediaId}/comments?limit=50&fields=id,text,timestamp,from.username` com paginação por `after` até esgotar; reply via `POST /v26.0/{commentId}/replies` com `message`. Reusa `EnsureSuccessAsync`.
6. Repositório Infra `PostgresCommentSuggestionRepository` + query em `PostgresPostRepository` para os últimos `Comments.LookbackPosts` posts publicados com `MetaMediaId` (novo método em `IPostRepository`: `GetLatestPublishedMediaIdsAsync(int limit)`).
7. `AppConfiguration` ganha `CommentsConfiguration { Enabled = false, LookbackPosts = 15 }`.
8. Unit tests: mapeamento de comentários, paginação, `ExistsByCommentId` idempotente.

**Arquivos-alvo:** `src/OddOddities.Domain/Interfaces/IMediaCommentPort.cs` (novo), `src/OddOddities.Domain/Interfaces/ICommentSuggestionRepository.cs` (novo), `src/OddOddities.Domain/Entities/CommentSuggestion.cs` (novo), `src/OddOddities.Domain/Entities/Post.cs`, `src/OddOddities.Domain/ValueObjects/AppConfiguration.cs`, `src/OddOddities.Infrastructure/Adapters/MetaInstagramPublishingAdapter.cs`, `src/OddOddities.Infrastructure/Adapters/PostgresCommentSuggestionRepository.cs` (novo), `src/OddOddities.Infrastructure/Adapters/PostgresPostRepository.cs`, `src/OddOddities.Infrastructure/Data/Configurations/EntityConfigurations.cs`, nova migration, DI Infrastructure, `tests/OddOddities.UnitTests/`.

**Dependencias:** nenhuma (independente dos RFs de vídeo).

---

## [x] RF-20: CommentSuggestionStep — classificação IA e decisão

**User Story:** Como dono, quero que comentários que sugerem temas virem input do post do dia, com classificação por IA e no máximo 1 sugestão por execução.

**Criterios de aceitacao:**

1. `CommentSuggestionStep : IPipelineStep` (StepName `CommentSuggestion`), registrado como PRIMEIRO step do pipeline (DI, antes de `TextGenerationStep`).
2. Short-circuits (todos `StepResult.Skipped()` com log): `Comments.Enabled = false`; run de vídeo (`context.IsVideoRun`); sem posts publicados com MetaMediaId; sem comentários novos.
3. Fluxo: busca últimos N posts publicados com `MetaMediaId` → `GetCommentsAsync` por media → filtra `CommentId` já existentes em `CommentSuggestions` → chama OpenRouter texto (cadeia free existente via `GetTextChainAsync`, 1 chamada) classificando cada comentário novo: é sugestão de tema factual adequado ao perfil? extrai `{theme, summary}`.
4. Registro em `CommentSuggestions`: todo comentário novo é persistido (`NotSuggestion`, `Rejected` ou `Accepted`) — idempotência por CommentId garante que nunca é reprocessado.
5. No máximo 1 sugestão `Accepted` por execução: apenas a primeira sugestão válida vira `Accepted`; as demais (mesmo classificadas como sugestão) recebem `Classification = NotSuggestion` com `RejectionReason = "LIMIT_ONE_SUGGESTION_PER_RUN"` (mantém idempotência e auditoria honesta — nunca são reprocessadas).
6. Sugestão aceita preenche `context.Suggestion = new SuggestionContext(Theme, Summary, AuthorUsername, CommentId, SourceCommentText)`; falha de classificação IA → warning, nenhuma sugestão, pipeline segue normal.
7. **Validação editorial (hash/similaridade) NÃO acontece neste step** — acontece dentro do `TextGenerationStep` (RF-21), quando existe texto para hashar.
8. Erro de permissão Meta (HTTP 403 ou body com codes 10/190/3 na mensagem da `HttpRequestException` embutida por `EnsureSuccessAsync`) → log warning `comment permission missing — skipping comment suggestion step`, step `Skipped`, pipeline segue (fallback do risco R14). Outros erros Meta → warning, `Skipped` (leitura de comentários nunca falha o dia).
9. DI: registrar port/adapter/step.
10. Unit tests: flag off; run de vídeo; sem novos comentários; classificação → Accepted/NotSuggestion; limite 1/execução; erro de permissão → skip com warning.

**Arquivos-alvo:** `src/OddOddities.Application/Steps/CommentSuggestionStep.cs` (novo), `src/OddOddities.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-13, RF-15 (contextos `IsVideoRun`/`Suggestion`), RF-19.

---

## [x] RF-21: TextGenerationStep com tema sugerido e crédito ao autor

**User Story:** Como dono, quero que o post do dia use o tema da sugestão quando houver, com crédito `Suggested by @user` na legenda.

**Criterios de aceitacao:**

1. `TextGenerationStep`: se `context.Suggestion != null`, a chamada de geração usa o tema/summary da sugestão como input (em vez de `context.Selection.CategoryName/SubcategoryName`), na cadeia de texto normal.
2. Validações existentes (length, hash, similaridade) aplicadas ao texto gerado do tema sugerido. Se rejeitado (hash/similaridade/length): registra `GenerationAttempt` `Rejected`, marca `CommentSuggestion.Rejected` com o mesmo motivo, limpa `context.Suggestion = null` e **re-tenta com o fluxo normal de categoria** (respeitando BR-006: o loop de tentativas continua; sugestão não consome tentativas extras).
3. Post criado com `SourceCommentSuggestionId = suggestion.Id` quando originado de sugestão; `Category`/`Subcategory` do post de sugestão = a menos usada (o `context.Selection` já vem do orchestrator — sem mudança).
4. Caption: quando de sugestão, `"...\n\nSuggested by @{AuthorUsername}\n\nSource: {SourceUrl}"` (ordem: texto, crédito, fonte); caso normal inalterado (`Source:` como hoje).
5. Unit tests: sugestão aceita → caption com crédito + `SourceCommentSuggestionId` setado; sugestão rejeitada por hash → `CommentSuggestion.Rejected` + fallback de categoria produz post; fluxo normal sem sugestão inalterado.

**Arquivos-alvo:** `src/OddOddities.Application/Steps/TextGenerationStep.cs`, `src/OddOddities.Domain/Interfaces/ICommentSuggestionRepository.cs` (uso), `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-15 (`SuggestionContext`), RF-19, RF-20.

---

## [x] RF-22: Reply pós-publicação e feature flag de comentários

**User Story:** Como dono, quero agradecer o autor da sugestão depois de publicar, sem que uma falha de reply afete a publicação.

**Criterios de aceitacao:**

1. `PublicationStep` ganha dependências `IMediaCommentPort` e `IOptions<AppConfiguration>` (não tem hoje — correção da seção 8.1 item 6).
2. Após publicar com sucesso E `context.Suggestion != null` E `Comments.Enabled`: `ReplyToCommentAsync(suggestion.CommentId, "Thanks for the suggestion!")` fire-and-forget — exceção → log warning, NUNCA falha o step nem marca Post Failed (a publicação já aconteceu).
3. Respeitar timeout próprio curto (ex.: 10 s) para o reply, não herdando o timeout do step.
4. Sem permissão (erro 403/codes 10/190/3): log warning único, sem retry.
5. Env vars de comentários no padrão do projeto: `AppConfiguration__Comments__Enabled=${COMMENTS_ENABLED:-false}`, `AppConfiguration__Comments__LookbackPosts=${COMMENTS_LOOKBACK_POSTS:-15}` em `docker-compose.yml` e `.env.example`.
6. Unit tests: reply chamado quando sugestão + flag on; NÃO chamado quando flag off ou sem sugestão; falha de reply não falha o step.

**Arquivos-alvo:** `src/OddOddities.Application/Steps/PublicationStep.cs`, `docker-compose.yml`, `.env.example`, `tests/OddOddities.UnitTests/`.

**Dependencias:** RF-15 (`SuggestionContext`), RF-19, RF-20, RF-21.

---

## [ ] RF-23: Documentação e ADRs do MVP 2

**User Story:** Como dono, quero a documentação atualizada refletindo o MVP 2 implementado, para manter o repositório como portfólio coerente.

**Criterios de aceitacao:**

1. `docs/architecture.md`: fluxo do pipeline com os 5 steps, portas novas (`IVideoGenerationPort`, `IMediaCommentPort`), tabela `CommentSuggestions`, regra BR-012 atualizada para "uma mídia por execução (imagem OU vídeo)", budgets por modalidade.
2. `docs/openrouter.md`: seção de vídeo (API assíncrona 4 passos, SKUs `per-video-second`, cadeia e tetos).
3. `docs/instagram-api.md`: seções Reels (`media_type=REELS`, `video_url`, polling maior) e comentários (`GET /{media-id}/comments`, `POST /{comment-id}/replies`, permissão `instagram_business_manage_comments` + nota para conferir o campo `permissions` do `refresh_access_token`).
4. ADR-009: "Pipeline alternativo de vídeo com steps skip-able" (decisão 4.3 + seção 8.2).
5. ADR-010: "Sugestões de tema via comentários com fallback de permissão" (decisões 3.2/4.2 + correção 8.1 item 4).
6. `docs/prd.md`: RFs RF-13..RF-22 copiados (ou referenciados) e marcados `[x]` após implementados; BR-012 atualizada.
7. Nenhuma mudança de código neste RF (apenas docs).

**Arquivos-alvo:** `docs/architecture.md`, `docs/openrouter.md`, `docs/instagram-api.md`, `docs/adr/ADR-009-*`, `docs/adr/ADR-010-*`, `docs/prd.md`.

**Dependências:** RF-18, RF-22 (documenta o que já está implementado).
