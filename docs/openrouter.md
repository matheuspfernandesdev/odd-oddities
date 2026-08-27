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

Para a POC, o custo estimado e de **USD 0,12/mes** considerando `meta/muse-image` a USD 0,01 por imagem. USD 5 cobrem cerca de 4 anos de execucao.

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

### Texto (gratuito)

- **Principal:** `google/gemma-4-26b-a4b-it:free`
  - Modelo multimodal leve.
  - Suporta `response_format` JSON Schema.
  - Custo: USD 0.
- **Fallback:** `openai/gpt-oss-20b:free`
  - Open-weight da OpenAI.
  - Custo: USD 0.

> Atencao: modelos gratuitos podem usar seus dados para melhorar os modelos do provedor. Nao envie informacoes sensiveis. Para esta POC o conteudo e publico.

### Imagem (pago)

- **Principal:** `meta/muse-image`
  - Preco atual: USD 0,01 por imagem.
  - 12 imagens/mes custam USD 0,12.
  - Suporta edicao, referencia e composicao.
- **Possiveis alternativas** (caso o principal fique indisponivel):
  - `google/gemini-2.5-flash-image`
  - `openai/gpt-image-1-mini`
  - `recraft/recraft-v4-styles` (requer imagem de referencia)

### Discovery API

Para verificar quais modelos estao disponiveis e seus precos atualizados:

```text
GET https://openrouter.ai/api/v1/models
GET https://openrouter.ai/api/v1/images/models
```

Filtre por:

- `output_modalities` (text ou image).
- `supported_parameters` (structured_outputs, aspect_ratio).

---

## 7. Configurar modelos no Worker

Os modelos sao configurados via variavel de ambiente:

| Variavel | Valor |
|---|---|
| `TEXT_MODEL_ID` | `google/gemma-4-26b-a4b-it:free` |
| `TEXT_FALLBACK_MODEL_ID` | `openai/gpt-oss-20b:free` |
| `IMAGE_MODEL_ID` | `meta/muse-image` |
| `IMAGE_FALLBACK_MODEL_ID` | _(pendente)_ |

Esses valores podem ser ajustados em **GitHub Actions Variables** sem rebuild da imagem.

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
  "model": "meta/muse-image",
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

## 10. Headers opcionais recomendados

- `HTTP-Referer`: URL publica do seu projeto (aparece no ranking do OpenRouter).
- `X-OpenRouter-Title`: Nome do projeto (aparece no ranking).

Esses headers **nao** sao obrigatorios, mas ajudam no ranking e na rastreabilidade.

---

## 11. Resiliencia implementada no Worker

- Retry com backoff exponencial (3 tentativas, 10s, 20s, 40s, cap 120s).
- Aplica-se a: timeout, erros de rede, HTTP 408, 429, 5xx.
- Logs estruturados com `modelId`, `costUsd`, `tokensIn`, `tokensOut`, `durationMs`.

---

## 12. Custos estimados

| Item | Custo mensal |
|---|---|
| 12 imagens x USD 0,01 | USD 0,12 |
| Texto (modelo gratuito) | USD 0,00 |
| **Total IA** | **USD 0,12** |

Para a POC, um credito de USD 5 dura cerca de 4 anos.

---

## 13. Troubleshooting

| Sintoma | Causa provavel | Solucao |
|---|---|---|
| 401 Unauthorized | Chave invalida ou revogada | Gerar nova chave |
| 402 Payment Required | Credito esgotado | Adicionar credito |
| 429 Too Many Requests | Rate limit | Aguardar e re-tentar |
| `no endpoints found that support tool use` | Modelo nao suporta `response_format` | Trocar para modelo compativel |
| Imagem Base64 ausente | Provedor retornou URL ou texto | Trocar para outro modelo de imagem |
| Custo maior que esperado | Outro modelo foi escolhido | Revisar `IMAGE_MODEL_ID` |

---

## 14. Referencias oficiais

- https://openrouter.ai/docs
- https://openrouter.ai/docs/quickstart
- https://openrouter.ai/docs/features/multimodal/image-generation
- https://openrouter.ai/docs/guides/overview/multimodal/image-generation
- https://openrouter.ai/docs/api/api-reference/images/generate-an-image
