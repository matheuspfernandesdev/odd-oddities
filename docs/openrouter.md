# Tutorial: OpenRouter - Conta, Chave de API e Modelos

Este tutorial cobre a configuracao do **OpenRouter** para uso pelo Worker do Odd Oddities: criacao de conta, geracao da chave de API, configuracao de creditos e escolha de modelos de texto e imagem.

---

## 1. O que e o OpenRouter

OpenRouter e um gateway que unifica o acesso a varios provedores de IA (OpenAI, Anthropic, Google, Meta, Mistral, Cohere, etc.) por meio de uma unica API e uma unica chave. Suporta tanto **texto** (`/chat/completions`) quanto **imagem** (`/images`).

---

## 2. Criar conta

1. Acesse https://openrouter.ai/.
2. Clique em **Sign in**.
3. Faca login com Google, GitHub ou e-mail.
4. Confirme o e-mail.

---

## 3. Adicionar creditos

Para modelos gratuitos, nenhum credito e necessario. Para modelos pagos:

1. Va em **Settings > Credits**.
2. Clique em **Add credits**.
3. Escolha o valor (minimo ~USD 5).
4. Pague com cartao.

Para a POC, o custo estimado e de **USD 0,0004/mes** considerando `google/gemini-3.1-flash-lite-image` a ~USD 0,00003 por imagem (12 imagens/mes). Alem disso, o Worker impoe um teto de **USD 0,05 por execucao do pipeline** (`ModelSelection:MaxCostPerRunUsd`).

---

## 4. Gerar chave de API

1. Va em **Settings > Keys**.
2. Clique em **Create Key**.
3. De um nome (ex: "odd-oddities-worker").
4. Defina um limite de gasto opcional (ex: USD 1/mes).
5. Copie a chave. **Ela so aparece uma vez.**
6. Armazene como `OPENROUTER_API_KEY` em GitHub Actions Secrets.

---

## 5. Limites de uso

- Cada chave pode ter um limite de credito proprio.
- Limites sao avaliados antes da requisicao.
- Em caso de credito esgotado, a API retorna `402 Payment Required`.

---

## 6. Modelos recomendados para o Odd Oddities

O Worker nao depende de uma lista fixa de fallback: **a cada execucao do pipeline** ele busca o catalogo de modelos na API do OpenRouter e monta uma cadeia de candidatos (preferido + dinamica). Veja "Resiliencia" na secao 11.

### Texto (gratuito)

- **Principal (config):** `google/gemma-4-26b-a4b-it:free`
  - Modelo multimodal leve.
  - Suporta `response_format` JSON.
  - Custo: USD 0.
- **Fallback dinamico:** demais modelos `:free` (ou pricing `0`), ordenados do mais barato ao mais caro, ate `ModelSelection:MaxTextModelAttempts` (default 5) modelos distintos. O catalogo de modelos free rotaciona com o tempo — por isso a busca e por execucao.

> Atencao: modelos gratuitos podem usar seus dados para melhorar os modelos do provedor. Nao envie informacoes sensiveis. Para esta POC o conteudo e publicado.

### Imagem (pago — nao existe modelo de imagem free)

- **Principal (config):** `google/gemini-3.1-flash-lite-image`
  - Preco: ~USD 0,00003 por imagem (`pricing.image_output`).
  - Modelo de imagem mais barato disponivel no catalogo (verificado em 2026-09).
- **Fallback dinamico:** demais modelos com `output_modalities` contendo `image`, ordenados por `image_output` asc, ate `ModelSelection:MaxImageModelAttempts` (default 3) e dentro de `MaxImageCostPerRequestUsd` (default USD 0,05).

> `meta/muse-image` **nao existe mais no catalogo** e foi substituido como default. Se a GitHub Variable `IMAGE_MODEL_ID` ainda apontar para ele, atualize o valor.

### Discovery API

O Worker usa estes endpoints (secao 11):

```text
GET https://openrouter.ai/api/v1/models?output_modalities=text&sort=pricing-low-to-high
GET https://openrouter.ai/api/v1/models?output_modalities=image
```

Cada modelo traz `pricing` (USD string por token/unidade), `architecture.output_modalities`, `context_length` etc. Free = id terminando em `:free` ou `pricing.prompt == "0"`.

---

## 7. Configurar modelos no Worker

Modelos preferidos (primeiro da cadeia de fallback) via variavel de ambiente:

| Variavel | Valor |
|---|---|
| `TEXT_MODEL_ID` | `google/gemma-4-26b-a4b-it:free` |
| `IMAGE_MODEL_ID` | `google/gemini-3.1-flash-lite-image` |

Nao ha mais `TEXT_FALLBACK_MODEL_ID`/`IMAGE_FALLBACK_MODEL_ID`: os fallbacks vem do catalogo dinamico.

Limites de fallback e custo (defaults no codigo; opcional no `appsettings.json`, secao `AppConfiguration:ModelSelection`):

| Config | Default | Descricao |
|---|---|---|
| `MaxTextModelAttempts` | 5 | Max de modelos distintos de texto por execucao |
| `MaxImageModelAttempts` | 3 | Max de modelos distintos de imagem por execucao |
| `MaxCostPerRunUsd` | 0.05 | Teto de custo total (texto+imagem) por execucao |
| `MaxTextCostPerRequestUsd` | 0.01 | Teto de custo estimado por request de texto para entrar na cadeia |
| `MaxImageCostPerRequestUsd` | 0.05 | Teto de custo estimado por request de imagem para entrar na cadeia |
| `MinContextLength` | 8000 | Contexto minimo para um modelo de texto ser candidato |

`TEXT_MODEL_ID`/`IMAGE_MODEL_ID` podem ser ajustados em **GitHub Actions Variables** sem rebuild da imagem. A secao `ModelSelection` nao precisa de env vars (defaults no codigo).

---

## 8. Exemplo de chamada - Texto

```text
POST https://openrouter.ai/api/v1/chat/completions
Authorization: Bearer <OPENROUTER_API_KEY>
Content-Type: application/json
HTTP-Referer: https://odd-oddities.exemplo.com
X-OpenRouter-Title: Odd Oddities Worker

{
  "model": "google/gemma-4-26b-a4b-it:free",
  "messages": [
    {
      "role": "system",
      "content": "You generate one factual curiosity..."
    },
    {
      "role": "user",
      "content": "Generate a curiosity about Science/Ocean."
    }
  ],
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "Curiosity",
      "schema": {
        "type": "object",
        "properties": {
          "textContent": { "type": "string" },
          "summary": { "type": "string" },
          "theme": { "type": "string" },
          "sourceUrl": { "type": "string" },
          "category": { "type": "string" },
          "subcategory": { "type": "string" }
        },
        "required": ["textContent","summary","theme","sourceUrl","category","subcategory"]
      }
    }
  }
}
```

Resposta esperada:

```json
{
  "choices": [
    { "message": { "content": "...JSON..." } }
  ],
  "usage": { "prompt_tokens": 123, "completion_tokens": 45, "total_tokens": 168 }
}
```

---

## 9. Exemplo de chamada - Imagem

```text
POST https://openrouter.ai/api/v1/images
Authorization: Bearer <OPENROUTER_API_KEY>
Content-Type: application/json

{
  "model": "google/gemini-3.1-flash-lite-image",
  "prompt": "A poetic surreal illustration about a luminous jellyfish drifting through deep ocean..."
}
```

Resposta esperada:

```json
{
  "data": [
    { "b64_json": "iVBORw0KGgoAAAANSUhEUgAA..." }
  ],
  "usage": { "cost": 0.01 }
}
```

A imagem vem em Base64. Decodifique, processe com ImageSharp, faca upload no MinIO.

---

## 9b. Geracao de video (MVP 2 — API assincrona)

A OpenRouter oferece geracao de video por uma **API assincrona** (diferente de texto e imagem, que sao sincronos). O Worker usa isso para gerar Reels curtos (≤ 8 s, 9:16, 480p) publicados 1 vez a cada 15 dias.

### Fluxo em 4 passos

```text
1. POST /api/v1/videos            → 202 { id, polling_url, status: "pending" }
2. GET  /api/v1/videos/{jobId}    → { status: "pending" | "in_progress" | "completed" | "failed" }
3. GET  /api/v1/videos/{jobId}/content?index=0   (com Authorization: Bearer) → MP4
4. usage.cost vem no poll response quando status = completed → vira CostUsd em GenerationAttempt
```

O Worker faz polling a cada `VideoJobPollingIntervalSeconds` (30 s) ate `completed`/`failed`, no maximo `MaxVideoJobPollingAttempts` (30 → cobre ~15 min). O download em (3) exige `Authorization: Bearer` — as `unsigned_urls` nao sao pre-assinadas.

### Request (submit)

```text
POST https://openrouter.ai/api/v1/videos
Authorization: Bearer <OPENROUTER_API_KEY>
Content-Type: application/json

{
  "model": "bytedance/seedance-2.0-mini",
  "prompt": "A poetic surreal short video about ...",
  "duration": 5,
  "resolution": "480p",
  "aspect_ratio": "9:16",
  "generate_audio": true
}
```

Resposta `202`:

```json
{ "id": "vid_abc123", "polling_url": "https://openrouter.ai/api/v1/videos/vid_abc123", "status": "pending" }
```

Poll `completed`:

```json
{ "status": "completed", "usage": { "cost": 0.08 } }
```

### Descoberta de modelos e SKUs `per-video-second`

```text
GET https://openrouter.ai/api/v1/videos/models
```

Retorna descritores com `supported_durations`, `supported_resolutions`, `supported_aspect_ratios` e `pricing_skus`. O catalogo de video usa SKUs **por segundo de video** (`per-video-second[-<res>]`), nao por token/imagem. O custo efetivo por segundo do modelo = **menor SKU `per-video-second*` valido**; modelo sem esse SKU nao e elegivel. Custo estimado do job = `custo/segundo × DurationSeconds`.

> Verificado em 2026-09: **nao existe modelo de video gratuito** no catalogo (zero SKUs a `0`). Melhor candidato: `bytedance/seedance-2.0-mini` (~USD 0,05–0,11 por video de 5 s). O `usage.cost` real do poll e a fonte da verdade e ja e acumulado no budget.

### Cadeia e tetos (padrao ADR-008)

`GetVideoChainAsync` segue o mesmo padrao das cadeias de texto/imagem: preferido (`AppConfiguration:Video:ModelId`) primeiro, depois free (se existir), depois mais barato por segundo. Filtros: `supported_aspect_ratios` contendo `Video.AspectRatio` (9:16) e `supported_durations` contendo `Video.DurationSeconds`. Limites:

| Config | Default | Descricao |
|---|---|---|
| `MaxVideoModelAttempts` | 3 | Max de modelos distintos de video por execucao |
| `MaxVideoCostPerRequestUsd` | 0.15 | Teto de custo estimado por request de video para entrar na cadeia |
| `MaxCostPerVideoRunUsd` | 0.20 | Teto de custo total (texto+video) por execucao de video |

Catalogo indisponivel → cadeia so com o modelo configurado (fallback silencioso, mesmo padrao dos catalogos de texto/imagem). Ver [ADR-009](./adr/ADR-009-pipeline-video-skip-able.md).

---

## 10. Headers opcionais recomendados

- `HTTP-Referer`: URL publica do seu projeto (aparece no ranking do OpenRouter).
- `X-OpenRouter-Title`: Nome do projeto (aparece no ranking).

Esses headers **nao** sao obrigatorios, mas ajudam no ranking e na rastreabilidade.

---

## 11. Resiliencia implementada no Worker

- **Busca do catalogo** (`GET /models`) no inicio de cada execucao do pipeline. Se falhar: log de warning e a cadeia fica apenas com o modelo da config (comportamento antigo). Nunca derruba o pipeline.
- **Fallback de modelo:** erro de API/transporte/modelo invalido em um candidato avanca para o proximo da cadeia (free -> mais barato, dentro dos tetos), ate os limites `MaxTextModelAttempts`/`MaxImageModelAttempts`.
- **Retry de conteudo:** rejeicoes de negocio (texto longo, hash duplicado, similaridade) re-tentam no mesmo modelo ate `MaxGenerationAttempts` (3).
- **Budget:** antes de cada chamada estima-se o custo e compara com `MaxCostPerRunUsd - acumulado`; o custo real (`usage.cost`) e acumulado apos cada chamada. Estourou: falha `BUDGET_EXCEEDED`.
- **Auditoria:** cada tentativa grava em `GenerationAttempt` (`ModelId`, `Status`, `CostUsd`, `TokensIn/Out`, `DurationMs`). Tentativas de texto antes da criacao do Post gravam `PostId = null`.
- Logs estruturados com `modelId`, `costUsd`, `tokensIn`, `tokensOut`, `durationMs`.

Ver [ADR-008](./adr/ADR-008-modelo-dinamico-fallback-custo.md).

---

## 12. Custos estimados

| Item | Custo mensal |
|---|---|
| 12 imagens x ~USD 0,00003 | ~USD 0,0004 |
| Texto (modelo gratuito) | USD 0,00 |
| **Total IA** | **< USD 0,01** |

Teto de seguranca por execucao: USD 0,05 (`MaxCostPerRunUsd`). Um credito de USD 5 dura anos.

---

## 13. Troubleshooting

| Sintoma | Causa provavel | Solucao |
|---|---|---|
| 401 Unauthorized | Chave invalida ou revogada | Gerar nova chave |
| 402 Payment Required | Credito esgotado | Adicionar credito |
| 429 Too Many Requests | Rate limit | Aguardar e re-tentar |
| `no endpoints found that support tool use` | Modelo nao suporta `response_format` | Trocar para modelo compativel |
| Imagem Base64 ausente | Provedor retornou URL ou texto | Trocar para outro modelo de imagem |
| Custo maior que esperado | Budget/estimativa incorreta | Revisar `ModelSelection` e `GenerationAttempt.CostUsd` |
| Todos os modelos falham (`ALL_MODELS_FAILED`) | Catalogo/config invalidos | Verificar `TEXT_MODEL_ID`/`IMAGE_MODEL_ID` e logs de fallback |

---

## 14. Referencias oficiais

- https://openrouter.ai/docs
- https://openrouter.ai/docs/quickstart
- https://openrouter.ai/docs/features/multimodal/image-generation
- https://openrouter.ai/docs/guides/overview/multimodal/image-generation
- https://openrouter.ai/docs/api/api-reference/images/generate-an-image
