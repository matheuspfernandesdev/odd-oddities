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
      "content": "You generate one factual curiosity... Editorial profile — Odd Oddities: strange, mysterious, little-known curiosities, not surface trivia; pick the most obscure angle... Depth over surface... NOT gore, decay, bodily fluids, torture or anything revolting. Mysterious ≠ disgusting... Length: 2-4 short paragraphs, between 800 and 1600 characters... imageFocus: one concrete, specific visual subject at the heart of the curiosity (e.g. \"a corroded ancient Greek bronze device with interlocking gears\"), not a vague mood... Respond with a JSON object containing these exact fields: textContent (...), summary (...), theme (...), imageFocus (max 300 characters), sourceUrl (...), category (...), subcategory (...)."
    },
    {
      "role": "user",
      "content": "Generate a curiosity about Science/Ocean. Choose the angle that best fits the editorial profile."
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
          "imageFocus": { "type": "string" },
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

`imageFocus` e opcional no parse: ausente no JSON → o pipeline usa `Theme` como foco da imagem (fallback).

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
  "prompt": "A poetic surreal illustration whose clear, unmistakable central subject is: <imageFocus ou Theme>. The subject must dominate the composition and be rendered with concrete, recognizable detail — not an abstract mood and not a generic scene. Artistic, dreamlike quality, suitable for Instagram. No text, letters, numbers or watermarks in the image."
}
```

O assunto central vem de `imageFocus` (gerado junto da curiosidade); quando ausente, o pipeline envia `Theme`.

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
