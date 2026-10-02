# Planejamento MVP 2.1 — Odd Oddities

> Planejamento do MVP 2.1: (1) legenda de texto até o dobro do tamanho (800 → 1600 caracteres); (2) perfil editorial "bizarro/misterioso" no prompt de geração de curiosidades; (3) imagem dirigida ao foco real da curiosidade via novo campo `imageFocus`.
>
> **Base de implementação: `main` (decisão do dono).** Numeração de RFs **RF-24..RF-28** — o MVP 2 ocupa RF-13..23 na branch `feat/mvp2-planning`.
>
> Documentos relacionados: [`mvp2-planning.md`](./mvp2-planning.md), [`architecture.md`](./architecture.md), [`openrouter.md`](./openrouter.md), [`prd.md`](./prd.md), [`adr/`](./adr/).

---

# 1. Resumo executivo

| # | Melhoria | Decisão | Custo | Risco principal |
|---|---|---|---|---|
| 1 | Texto maior | `AppConfiguration.MaxCaptionContentLength` default 800 → **1600**; prompt de texto interpolado pela config; guarda nova de caption total ≤ **2200** (limite do Instagram) | ~0 (modelos free) | Baixo — rejeição `TEXT_TOO_LONG` já tem retry BR-006 |
| 2 | Temas mais "bizarros" | Reescrita do system+user prompt com **perfil editorial oddity**: incomum, misterioso, profundo, ancorado em fatos concretos (descobertas, enigmas, achados arqueológicos), **sem** conteúdo nojento | ~0 (+~400 prompt tokens) | Médio — modelo `:free` pode ignorar o brief (sem validação automática de conteúdo) |
| 3 | Imagem com foco | Novo campo **`imageFocus`** gerado junto da curiosidade pelo modelo de texto → persistido no `Post` (migration) → vira o assunto central do prompt de imagem (fallback: `Theme`) | ~0 | Baixo — fallback para `Theme` mantém comportamento atual (nunca pior) |

**Total estimado: 2,5–3 dias** (mais 0,5 dia de validação manual end-to-end). Custo mensal inalterado (~USD 6).

---

# 2. Análise do estado atual (evidências do código)

| Problema | Onde está hoje |
|---|---|
| Limite 800 hardcoded no prompt | `src/OddOddities.Infrastructure/Adapters/OpenRouterTextGenerationAdapter.cs:74` — `"textContent (the curiosity, max 800 characters)"` |
| Limite 800 na validação | `src/OddOddities.Application/Steps/TextGenerationStep.cs:200` usa `_config.Value.MaxCaptionContentLength`; default em `AppConfiguration.cs:14` = 800; **não há** override em `appsettings.json` |
| Seed `MAX_CAPTION_CONTENT_LENGTH=800` | `src/OddOddities.Infrastructure/Data/OddOdditiesDbContext.cs:98` (informativo — não é lido em runtime) |
| Teste de limite usa 900 chars | `tests/OddOddities.UnitTests/TextGenerationStepTests.cs:141` |
| Prompt de texto sem identidade editorial | `OpenRouterTextGenerationAdapter.cs:71-86` — só "factual, not opinion-based, not offensive (BR-001)" + `"Generate a curiosity about {category}/{subcategory}."` |
| Imagem recebe só o `Theme` (rótulo ≤ 120 chars) | `src/OddOddities.Application/Steps/ImageGenerationStep.cs:111-114` → `GenerateImageAsync(text.Theme, ...)` |
| Wrapper genérico da imagem | `src/OddOddities.Infrastructure/Adapters/OpenRouterImageGenerationAdapter.cs:61-63` — `"A poetic surreal illustration about {prompt}."` |
| Payload sem campo visual | `src/OddOddities.Infrastructure/Adapters/CuriosityJsonParser.cs:117-136` — campos `textContent/summary/theme/sourceUrl/category/subcategory` |
| Estimativa de tokens para budget | `src/OddOddities.Domain/Constants/PipelineConstants.cs:77-82` — `EstimatedPromptTokens=1500`, `EstimatedCompletionTokens=600` |

**Sintomas relatados pelo dono (por que estamos fazendo isso):**

1. Posts com texto curto demais (legenda pequena, poderia ser até o dobro).
2. Temas triviais demais — ex.: curiosidade sobre a pata de uma ave. O desejado é linha dos achados de **Peter Lund em Lagoa Santa (preguiças-gigantes que possivelmente conviveram com humanos)**, mecanismo de Antikythera, enigmas arqueológicos — misterioso/bizarro, mas **não necessariamente nojento**.
3. Imagens genéricas — ex.: texto sobre o mecanismo de Antikythera, imagem sem o mecanismo; texto sobre os pés zifodáctilos do pica-pau, imagem sem o pé. O prompt da imagem precisa **identificar o foco da curiosidade e priorizá-lo**.

---

# 3. Melhoria 1 — Legenda até o dobro (800 → 1600)

## 3.1 Decisões

1. `AppConfiguration.MaxCaptionContentLength` default **800 → 1600** (`AppConfiguration.cs:14`) + explícito em `appsettings.json` e `appsettings.Development.json` (`"MaxCaptionContentLength": 1600`).
2. Prompt de texto passa a ser **interpolado pela config** (o adapter já recebe `IOptions<AppConfiguration>`; lê `MaxCaptionContentLength` — sem mudar a assinatura da port):
   - `"textContent (the curiosity: 2–4 short paragraphs, between {max/2} and {max} characters)"`.
   - O modelo recebe uma faixa (800–1600) em vez de um teto seco → texto longo o suficiente, com folga abaixo do limite.
3. **Guarda nova de caption total**: `PipelineConstants.InstagramMaxCaptionLength = 2200` (limite real de legenda do Instagram). `TextGenerationStep` valida a caption final (`TextContent + "\n\nSource: ..." [+ "Suggested by @user"]`) e rejeita com código `CAPTION_TOO_LONG` se estourar. Defensivo: 1600 + fonte (~80) + crédito (~30) ≈ 1710, folga preservada mesmo em cenários futuros.
4. Seed `MAX_CAPTION_CONTENT_LENGTH` 800 → 1600 (migration de HasData — consistência do portfólio; o valor não é lido em runtime).
5. Estimativas de budget: `EstimatedPromptTokens` 1500 → 2000 e `EstimatedCompletionTokens` 600 → 1200 (prompt mais longo + texto 2× maior; mantém o cálculo pre-call honesto dentro de `MaxTextCostPerRequestUsd`).
6. **BR-002 vira**: `TextContent <= MaxCaptionContentLength (default 1600)`. Atualizar `docs/prd.md` e `docs/architecture.md`.

## 3.2 Impacto em custo

| Item | Antes | Depois |
|---|---|---|
| Texto (modelo `:free`) | USD 0 | USD 0 (≈ +400 prompt tokens, +~400 completion tokens) |
| Texto (fallback pago, pior caso) | ≤ USD 0,01/req (`MaxTextCostPerRequestUsd`) | inalterado — cabe 2× texto |
| **Total mensal** | ~USD 6 | **~USD 6** |

---

# 4. Melhoria 2 — Perfil editorial "oddity" no prompt de texto

## 4.1 Decisões

- Reescrita do system prompt em `OpenRouterTextGenerationAdapter.cs:71-86`, **mantendo** o JSON schema, `response_format: json_object` e BR-001 (factual, não ofensivo).
- Brief editorial **hardcoded no adapter** (KISS — texto longo em env var é frágil; ajuste = deploy).
- Categorias/subcategorias do seed **não mudam**: o ângulo estranho vem do brief, não da categoria. Alterar seeds de subcategorias = melhoria futura (fora do escopo).
- **Sem validação IA editorial extra** (sem chamada adicional de classificação): o dono pediu mudança de prompt; filtro automático = melhoria futura.

## 4.2 Conteúdo do brief (em inglês, no código)

```text
Editorial profile — Odd Oddities:
- We publish strange, mysterious, little-known curiosities ("oddities"), not surface trivia.
- Pick the most obscure angle of the requested category: real discoveries, historical
  enigmas, contested archaeology, lost or ancient technology, anomalies and unexplained
  but documented phenomena (e.g. Peter Lund's giant ground sloths in Lagoa Santa possibly
  coexisting with early humans; the Antikythera mechanism).
- Depth over surface: anchor the text in a concrete place, person, date, number or finding.
  A mere anatomical or functional fact is too shallow unless it leads to something stranger.
- Factual and verifiable — sourceUrl must be a credible source for the specific claim (BR-001).
- NOT gore, decay, bodily fluids, torture or anything revolting. Mysterious ≠ disgusting.
- Not opinion-based, not offensive.
- Length: 2–4 short paragraphs, between {max/2} and {max} characters.
- imageFocus: one concrete, specific visual subject at the heart of the curiosity
  (name the exact object, creature or scene — e.g. "a corroded ancient Greek bronze
  device with interlocking gears"), not a vague mood.
```

- User message vira: `"Generate a curiosity about {category}/{subcategory}. Choose the angle that best fits the editorial profile."`
- O exemplo `imageFocus` acima pertence ao RF-26; no RF-25 o brief ainda não o contém (RFs sequenciais no mesmo arquivo).

---

# 5. Melhoria 3 — Campo `imageFocus` e prompt de imagem dirigido

## 5.1 Fluxo novo

```text
TextGeneration (JSON inclui imageFocus)
  → CuriosityJsonParser (campo opcional, nunca falha o parse se ausente)
  → TextGenerationResult.ImageFocus
  → Post.ImageFocus (VARCHAR(300) NULL — migration) + TextContext.ImageFocus
  → ImageGenerationStep envia text.ImageFocus (fallback: text.Theme)
  → OpenRouterImageGenerationAdapter monta o prompt com regra de foco
```

## 5.2 Prompt de imagem novo

Mantém a identidade "poetic surreal" da marca e acrescenta a regra de foco:

```text
A poetic surreal illustration whose clear, unmistakable central subject is: {focus}.
The subject must dominate the composition and be rendered with concrete, recognizable
detail — not an abstract mood and not a generic scene.
Artistic, dreamlike quality, suitable for Instagram.
No text, letters, numbers or watermarks in the image.
```

## 5.3 Decisões

- Assinatura da port `IImageGenerationPort.GenerateImageAsync(prompt, modelId, ct)` **não muda** — o `ImageGenerationStep` decide o que passar (hoje `text.Theme`, amanhã `text.ImageFocus`).
- `imageFocus` vazio/ausente no JSON → fallback `Theme` → comportamento **idêntico ao de hoje** (nunca pior).
- **Persistência**: coluna `Post.ImageFocus` (VARCHAR(300), nullable) + `PostConfiguration` — permite auditar/debugar imagens ruins depois (qual foco foi usado).
- Migration: `AddPostImageFocus` (RF-26) — gerada **depois** da migration de seed do RF-24; duas migrations pequenas, cada uma no seu RF, para cada RF compilar/testar isoladamente (mesma doutrina do MVP 2).

---

# 6. Análise arquitetural — o que muda em cada camada

## 6.1 Domain (`OddOddities.Domain`)

| Item | Tipo | Descrição |
|---|---|---|
| `PipelineConstants.InstagramMaxCaptionLength` | constante nova | `2200` — limite total da legenda no Instagram |
| `PipelineConstants.EstimatedPromptTokens` | alterar | 1500 → 2000 |
| `PipelineConstants.EstimatedCompletionTokens` | alterar | 600 → 1200 |
| `AppConfiguration.MaxCaptionContentLength` | alterar default | 800 → 1600 |
| `TextGenerationResult` | +campo | `string ImageFocus` |
| `Post.ImageFocus` | coluna nova | `VARCHAR(300)` nullable |

## 6.2 Application

| Item | Tipo | Descrição |
|---|---|---|
| `TextGenerationStep` | modificar | valida caption total ≤ 2200 (`CAPTION_TOO_LONG`); grava `Post.ImageFocus` (fallback `result.Theme`); preenche `TextContext.ImageFocus` (fallback `Theme`) |
| `PipelineContext.TextContext` | +campo | 8º positional record param `ImageFocus` (default `string.Empty` na inicialização de `PipelineContext.Text`) |
| `ImageGenerationStep` | modificar | `GenerateImageAsync(text.ImageFocus, ...)` com fallback `text.Theme` quando vazio |

## 6.3 Infrastructure

| Item | Tipo | Descrição |
|---|---|---|
| `OpenRouterTextGenerationAdapter` | modificar | system prompt: brief editorial + faixa de tamanho interpolada por `_options.MaxCaptionContentLength`; user message com "editorial profile"; schema JSON + `imageFocus` |
| `CuriosityJsonParser.CuriosityPayload` | +campo | `[JsonPropertyName("imageFocus")] string? ImageFocus` |
| `OpenRouterImageGenerationAdapter` | modificar | wrapper novo com regra de "clear, unmistakable central subject" |
| `EntityConfigurations` | modificar | configuração de `Post.ImageFocus` |
| Migration `UpdateMaxCaptionContentLengthSeed` | EF Core | HasData 800 → 1600 (RF-24) |
| Migration `AddPostImageFocus` | EF Core | coluna `ImageFocus` (RF-26) |

## 6.4 Worker

- `appsettings.json` e `appsettings.Development.json`: `"AppConfiguration": { "MaxCaptionContentLength": 1600 }`.
- Env var opcional: `AppConfiguration__MaxCaptionContentLength=1600` (o default em código já resolve sem ela).
- `docker-compose.yml`: sem mudança obrigatória.

## 6.5 Testes

| Arquivo | Mudança |
|---|---|
| `TextGenerationStepTests.cs` | `TEXT_TOO_LONG` usa 1700 chars; novo teste `CAPTION_TOO_LONG`; helper `Ok(...)` passa `imageFocus` |
| `ImageGenerationStepTests.cs` | `TextContext` com 8 args; teste envia `ImageFocus`; teste fallback `Theme` |
| `CuriosityJsonParserTests.cs` | parse de `imageFocus` presente / ausente (ausente → null, sem exceção) |
| **Novo** `OpenRouterTextGenerationAdapterTests.cs` | padrão `StubHandler` capturando `RequestBodies` (mesmo padrão já usado no repo): asserta "max 1600 characters" (ou a faixa), brief editorial e campo `imageFocus` no schema |
| **Novo** `OpenRouterImageGenerationAdapterTests.cs` | asserta "central subject" + foco presente no prompt enviado |

---

# 7. Custos consolidados (MVP 2.1)

| Item | Mensal | Observação |
|---|---|---|
| VPS + domínio + infra | USD 6,00 | Inalterado |
| Texto (curiosidades) | USD 0 | Modelos free; tokens maiores, custo zero |
| Imagens | USD 0,0004 | Inalterado |
| Meta API | USD 0 | Gratuita |
| **Total** | **~USD 6,00/mês** | **Sem variação vs MVP 1** |

---

# 8. Riscos e mitigações

| ID | Risco | Impacto | Prob. | Mitigação |
|---|---|---|---|---|
| R-01 | Texto gerado > 1600 → rejeição em loop (`TEXT_TOO_LONG`) | Médio | Baixa | Prompt pede faixa `max/2..max` com folga; retry BR-006 (3×) + cadeia de modelos |
| R-02 | Modelo `:free` ignora o brief editorial → temas continuam triviais | Médio | **Média** | Sem validação automática (escopo); validar manualmente no pós-deploy; validação IA de editorial = melhoria futura |
| R-03 | `imageFocus` vago, genérico ou ausente no JSON | Baixo | Média | Fallback `Theme` = comportamento atual; nunca regredir |
| R-04 | Caption estourar o limite de 2200 do Instagram → erro da Meta | Médio | Baixa | Guarda nova `CAPTION_TOO_LONG` antes de publicar |
| R-05 | **Conflito de merge com `feat/mvp2-planning`** (mesmos arquivos) | Médio | Alta | Seção 12 lista os conflitos; adapters de prompt **não** são tocados pelo MVP 2; resolver no merge aceitando os dois lados |
| R-06 | Migration falha no startup | Alto | Baixa | Padrão já existente (`ApplyMigrationsHostedService`); 2 migrations pequenas e idempotentes |
| R-07 | Prompt de imagem mais longo sair caro/lento | Baixo | Baixa | Custo de imagem é por imagem, não por token — inalterado |

---

# 9. Plano de implementação

## 9.1 Ordem de trabalho (estimativas para 1 dev)

### Fase 1 — Tamanho de texto (RF-24) — 0,5 dia

1. `AppConfiguration.MaxCaptionContentLength` default 1600 + `appsettings*.json`.
2. `PipelineConstants`: `InstagramMaxCaptionLength = 2200`, tokens estimados 2000/1200.
3. Prompt interpolado pela config no `OpenRouterTextGenerationAdapter`.
4. Guarda `CAPTION_TOO_LONG` no `TextGenerationStep`.
5. Seed 1600 + migration `UpdateMaxCaptionContentLengthSeed`.
6. Unit tests (limite 1700, caption > 2200, teste de adapter com StubHandler).

### Fase 2 — Perfil editorial (RF-25) — 0,5 dia

1. Reescrita do system prompt (brief oddity) + user message no adapter.
2. Teste de adapter: corpo da requisição contém o brief e as exclusões (gore/disgusting).

### Fase 3 — Foco da imagem (RF-26 + RF-27) — 1–1,5 dia

1. RF-26: `CuriosityPayload.ImageFocus` → `TextGenerationResult.ImageFocus` → `Post.ImageFocus` (+ EntityConfigurations + migration `AddPostImageFocus`) → `TextContext.ImageFocus` → gravação no `TextGenerationStep` com fallback.
2. RF-27: `ImageGenerationStep` envia `text.ImageFocus` (fallback `Theme`); wrapper novo no `OpenRouterImageGenerationAdapter`.
3. Unit tests: parser, step de texto, step de imagem (fallback), adapter de imagem.

### Fase 4 — Documentação e deploy (RF-28) — 0,5 dia

1. `docs/prd.md`: BR-002 atualizada + RFs RF-24..28 copiados e marcados `[x]`.
2. `docs/architecture.md`: BR-002, fluxo com `imageFocus`, novos campos.
3. `docs/openrouter.md`: exemplos de prompt de texto e imagem atualizados.
4. ADR-011: "Perfil editorial oddity e imagem dirigida por foco".
5. Deploy + **validação manual end-to-end** (0,5 dia): 1 execução real verificando tamanho, tema e foco da imagem.

**Total estimado: 2,5–3 dias (+0,5 validação).**

## 9.2 Gates de verificação

```powershell
dotnet build OddOddities.slnx
dotnet test tests\OddOddities.UnitTests\OddOddities.UnitTests.csproj
dotnet format

# Migration (|RF-24 e RF-26)
dotnet ef migrations add UpdateMaxCaptionContentLengthSeed --project src\OddOddities.Infrastructure --startup-project src\OddOddities.Worker
dotnet ef migrations add AddPostImageFocus --project src\OddOddities.Infrastructure --startup-project src\OddOddities.Worker
```

## 9.3 Critérios de aceite do MVP 2.1

- [ ] Post gerado com `TextContent` entre 800 e 1600 chars; caption final < 2200; publicado sem `TEXT_TOO_LONG`/`CAPTION_TOO_LONG`.
- [ ] Prompt de texto contém o perfil editorial (misterioso/profundo, com exemplos, sem gore) — verificável por teste de adapter.
- [ ] Prompt de texto declara `imageFocus` no schema JSON; valor parseado e persistido em `Post.ImageFocus`.
- [ ] Prompt de imagem cita o foco como "clear, unmistakable central subject".
- [ ] `imageFocus` ausente → fallback `Theme` (comportamento atual preservado).
- [ ] Migrations aplicam no startup sem passo manual; seed `MAX_CAPTION_CONTENT_LENGTH=1600`.
- [ ] Validação manual: gerar 1 post real e confirmar (a) texto longo, (b) tema incomum/misterioso, (c) imagem com o asserto central da curiosidade.
- [ ] `dotnet build` + `dotnet test` verdes.

---

# 10. Revisão (2026-10-01)

Revisão do planejamento contra o código atual em `main` (`OpenRouterTextGenerationAdapter`, `OpenRouterImageGenerationAdapter`, `TextGenerationStep`, `ImageGenerationStep`, `CuriosityJsonParser`, `PipelineContext`, `AppConfiguration`, seeds, testes) e contra o estado da `feat/mvp2-planning` (RF-13..23 implementados). Os RFs da seção 11 são a **fonte de verdade para implementação**.

## 10.1 Decisões finais de design (resumo)

1. **Tamanho**: limite único `MaxCaptionContentLength = 1600` (código + appsettings) + guarda de caption total 2200. Faixa pedida ao modelo: `max/2..max`. BR-002 redefinida como dependente da config.
2. **Editorial**: brief hardcoded no adapter de texto (KISS), sem configuração por env var e sem validação IA extra.
3. **Foco da imagem**: campo `imageFocus` **gerado pelo modelo de texto** (que já leu a curiosidade inteira) em vez de enviar o texto integral ao modelo de imagem — prompt curto, específico e barato. Persistido no `Post` para auditoria.
4. **Fallback**: `imageFocus` ausente → `Theme` → comportamento idêntico ao atual. Nenhum caminho regride.
5. **RFs independentes**: cada RF compila e testa isoladamente (seção 11); ordem RF-24 → 25 → 26 → 27 → 28.
6. **Base `main`**: aceitamos os conflitos de merge documentados na seção 12 (adapters de prompt intocados pelo MVP 2).

---

# 11. Requisitos Funcionais — MVP 2.1

> **Atenção ao `/implement-rf`:** ele extrai RFs apenas de `docs/prd.md`. Após implementar, copiar esta seção para o `prd.md` (ou ajustar o command) e marcar `[x]`. RFs seguem a numeração do MVP 2 (RF-13..23) para não haver colisão.

## [x] RF-24: Caption até 1600 caracteres

**User Story:** Como dono, quero que os posts tenham texto de até o dobro do tamanho atual, para publicações mais substanciais no Instagram.

**Criterios de aceitacao:**

1. `AppConfiguration.MaxCaptionContentLength` default muda de 800 para **1600**; valor explícito em `appsettings.json` e `appsettings.Development.json`.
2. `OpenRouterTextGenerationAdapter` interpola o limite da config no system prompt: faixa `between {max/2} and {max} characters` (sem assinatura de port alterada — o adapter já recebe `IOptions<AppConfiguration>`).
3. `PipelineConstants` ganha `InstagramMaxCaptionLength = 2200`; `EstimatedPromptTokens = 2000`; `EstimatedCompletionTokens = 1200`.
4. `TextGenerationStep` valida a caption final completa (`TextContent + "\n\nSource: ..." [+ crédito de sugestão quando existir]`); acima de 2200 → rejeição `CAPTION_TOO_LONG` (mesmo fluxo de `TEXT_TOO_LONG`: registro em `GenerationAttempt`, retry BR-006).
5. Seed `MAX_CAPTION_CONTENT_LENGTH` atualizado para "1600" (migration `UpdateMaxCaptionContentLengthSeed`, auto-aplicada no startup).
6. Validação de `TextContent` continua usando `_config.Value.MaxCaptionContentLength` (nada hardcoded).
7. Unit tests: texto de 1700 chars → `TEXT_TOO_LONG`; caption > 2200 → `CAPTION_TOO_LONG`; teste de adapter afirma a faixa de tamanho no corpo da requisição.
8. `dotnet build` e `dotnet test` verdes após o RF.

**Arquivos-alvo:** `src/OddOddities.Domain/ValueObjects/AppConfiguration.cs`, `src/OddOddities.Domain/Constants/PipelineConstants.cs`, `src/OddOddities.Infrastructure/Adapters/OpenRouterTextGenerationAdapter.cs`, `src/OddOddities.Application/Steps/TextGenerationStep.cs`, `src/OddOddities.Infrastructure/Data/OddOdditiesDbContext.cs`, nova migration em `src/OddOddities.Infrastructure/Migrations/`, `src/OddOddities.Worker/appsettings.json`, `src/OddOddities.Worker/appsettings.Development.json`, `tests/OddOddities.UnitTests/TextGenerationStepTests.cs`, novo `tests/OddOddities.UnitTests/OpenRouterTextGenerationAdapterTests.cs`.

**Dependencias:** nenhuma.

---

## [x] RF-25: Perfil editorial oddity no prompt de texto

**User Story:** Como dono, quero que as curiosidades sejam estranhas, misteriosas e profundas (ex.: preguiças-gigantes de Peter Lund em Lagoa Santa convivendo com humanos), e não fatos triviais de superfície — sem conteúdo nojento.

**Criterios de aceitacao:**

1. System prompt do `OpenRouterTextGenerationAdapter` inclui o brief editorial "Odd Oddities" (seção 4.2): incomum/misterioso, ângulo mais obscuro da categoria, profundidade ancorada em lugar/pessoa/data/número, exemplos de referência, exclusão explícita de gore/decay/repulsivo, manutenção de BR-001 (factual + `sourceUrl` crível) e faixa de tamanho.
2. User message vira `Generate a curiosity about {category}/{subcategory}. Choose the angle that best fits the editorial profile.`
3. JSON schema, `response_format: json_object` e campos obrigatórios **inalterados** neste RF (o `imageFocus` entra no RF-26).
4. Categorias/subcategorias do seed **inalteradas**.
5. Brief é texto fixo no código (não configurável por env var).
6. Unit test de adapter (`OpenRouterTextGenerationAdapterTests`) afirma que o corpo da requisição contém marcadores do brief (ex.: "myster", "GORE"/"disgusting" como exclusão, "editorial profile") e o prompt do usuário com o pedido de ângulo.
7. `dotnet build` e `dotnet test` verdes após o RF.

**Arquivos-alvo:** `src/OddOddities.Infrastructure/Adapters/OpenRouterTextGenerationAdapter.cs`, `tests/OddOddities.UnitTests/OpenRouterTextGenerationAdapterTests.cs`.

**Dependencias:** RF-24 (mesmo arquivo — ordem sequencial).

---

## [x] RF-26: Campo imageFocus no payload de texto e no Post

**User Story:** Como dono, quero que a geração de texto identifique o assunto visual central da curiosidade e o persista, para que a imagem seja gerada com foco no assunto certo.

**Criterios de aceitacao:**

1. System prompt do adapter de texto declara o campo `imageFocus` (max 300 chars): um sujeito visual concreto e específico no centro da curiosidade (ex.: "a corroded ancient Greek bronze device with interlocking gears"), não um clima vago.
2. `CuriosityPayload` ganha `[JsonPropertyName("imageFocus")] string? ImageFocus`; ausente no JSON → `null` (nunca falha o parse — `CuriosityJsonParserTests` cobre presente e ausente).
3. `TextGenerationResult` ganha `string ImageFocus` (Domain).
4. `Post.ImageFocus` (VARCHAR(300), nullable) + configuração EF (`EntityConfigurations`) + migration `AddPostImageFocus` auto-aplicada no startup.
5. `TextGenerationStep` grava `Post.ImageFocus = result.ImageFocus` (fallback `result.Theme` quando vazio) e preenche `TextContext.ImageFocus` com a mesma regra.
6. `PipelineContext.TextContext` ganha o campo `ImageFocus` (8º parâmetro do record; inicialização padrão de `PipelineContext.Text` ajustada).
7. `TextGenerationResult`/`Post` de sugestão de comentário (MVP 2, quando aplicável) usam a mesma regra de fallback.
8. Unit tests: payload com/sem `imageFocus`; step grava `ImageFocus` no Post; fallback para `Theme` quando vazio.
9. `dotnet build` e `dotnet test` verdes após o RF.

**Arquivos-alvo:** `src/OddOddities.Infrastructure/Adapters/CuriosityJsonParser.cs`, `src/OddOddities.Infrastructure/Adapters/OpenRouterTextGenerationAdapter.cs`, `src/OddOddities.Domain/Interfaces/ITextGenerationPort.cs`, `src/OddOddities.Domain/Entities/Post.cs`, `src/OddOddities.Infrastructure/Data/Configurations/EntityConfigurations.cs`, nova migration `AddPostImageFocus`, `src/OddOddities.Application/Pipeline/PipelineContext.cs`, `src/OddOddities.Application/Steps/TextGenerationStep.cs`, `tests/OddOddities.UnitTests/CuriosityJsonParserTests.cs`, `tests/OddOddities.UnitTests/TextGenerationStepTests.cs`.

**Dependencias:** RF-24.

---

## [x] RF-27: Prompt de imagem dirigido ao foco

**User Story:** Como dono, quero que a imagem priorize o assunto da curiosidade (o mecanismo de Antikythera, o pé do pica-pau), para deixar de sair genérica.

**Criterios de aceitacao:**

1. `ImageGenerationStep` chama `GenerateImageAsync(text.ImageFocus, ...)`; quando `ImageFocus` for vazio/null → envia `text.Theme` (fallback = comportamento atual).
2. `OpenRouterImageGenerationAdapter` monta o prompt: `"A poetic surreal illustration whose clear, unmistakable central subject is: {focus}."` + regra de dominância/composição concreta + estilo "Artistic, dreamlike quality, suitable for Instagram" + "No text, letters, numbers or watermarks".
3. Assinatura de `IImageGenerationPort` inalterada.
4. Identidade visual da marca (poetic surreal) preservada — só a regra de foco é acrescentada.
5. Unit tests: step envia `ImageFocus` (não `Theme`); step envia `Theme` quando `ImageFocus` vazio; teste de adapter afirma "central subject" e o valor do foco no corpo da requisição.
6. Validação manual: 1 post real com curiosidade de sujeito claro (ex.: mecanismo de Antikythera) e imagem contendo o sujeito.
7. `dotnet build` e `dotnet test` verdes após o RF.

**Arquivos-alvo:** `src/OddOddities.Application/Steps/ImageGenerationStep.cs`, `src/OddOddities.Infrastructure/Adapters/OpenRouterImageGenerationAdapter.cs`, `tests/OddOddities.UnitTests/ImageGenerationStepTests.cs`, novo `tests/OddOddities.UnitTests/OpenRouterImageGenerationAdapterTests.cs`.

**Dependencias:** RF-26.

---

## [x] RF-28: Documentação e ADR do MVP 2.1

**User Story:** Como dono, quero a documentação atualizada refletindo o MVP 2.1, para manter o repositório como portfólio coerente.

**Criterios de aceitacao:**

1. `docs/prd.md`: BR-002 atualizada (`TextContent <= MaxCaptionContentLength (default 1600)`); RFs RF-24..28 copiados (ou referenciados) e marcados `[x]` após implementados.
2. `docs/architecture.md`: limite 1600 + guarda 2200, brief editorial, fluxo `imageFocus` (texto → Post → imagem), campo `Post.ImageFocus`.
3. `docs/openrouter.md`: exemplos de prompt de texto (brief editorial, faixa de tamanho, campo `imageFocus`) e de imagem (regra de central subject) atualizados.
4. `docs/adr/ADR-011-perfil-editorial-e-foco-de-imagem.md`: captura as decisões da seção 10.1 (brief hardcoded, `imageFocus` gerado pelo texto, fallback `Theme`, persistência para auditoria).
5. Nenhuma mudança de código neste RF (apenas docs).
6. `.env.example`/`docker-compose.yml`: apenas se a env var opcional `AppConfiguration__MaxCaptionContentLength` for exposta (decisão na implementação; default em código dispensa).

**Arquivos-alvo:** `docs/prd.md`, `docs/architecture.md`, `docs/openrouter.md`, `docs/adr/ADR-011-perfil-editorial-e-foco-de-imagem.md`, opcionalmente `.env.example` e `docker-compose.yml`.

**Dependencias:** RF-24, RF-25, RF-26, RF-27 (documenta o que já está implementado).

---

# 12. Conflitos previstos com a `feat/mvp2-planning`

O MVP 2 está completo mas **não mergeado**. Ao mesclar as duas branches, estes arquivos são tocadas pelos dois lados:

| Arquivo | MVP 2 (RF) | MVP 2.1 (RF) |
|---|---|---|
| `src/OddOddities.Application/Steps/TextGenerationStep.cs` | RF-21 (tema sugerido + crédito) | RF-24 (guarda 2200), RF-26 (grava ImageFocus) |
| `src/OddOddities.Application/Pipeline/PipelineContext.cs` | RF-15/20 (`IsVideoRun`, `CostCeilingUsd`, `Suggestion`, `Video`) | RF-26 (`TextContext.ImageFocus`) |
| `src/OddOddities.Application/Steps/ImageGenerationStep.cs` | budget via `context.CostCeilingUsd` (remove `IOptions`) | RF-27 (envia `ImageFocus`) |
| `tests/.../TextGenerationStepTests.cs` | +RF-21 | RF-24/26 |
| `tests/.../ImageGenerationStepTests.cs` | budget novo | RF-27 |
| `src/OddOddities.Domain/Constants/PipelineConstants.cs` | constantes de vídeo/comentários | RF-24 (caption/tokens) |
| **Adapters de prompt (texto/imagem)** | **não tocados pelo MVP 2** | RF-25/27 |

**Regra de resolução**: nenhuma alteração se sobrescreve semanticamente — no merge, aceitar os dois lados (novas propriedades/métodos convivem). `VideoGenerationStep` do MVP 2 deriva o prompt do vídeo da mesma diretriz surrealista da imagem; após o merge, considerar aplicar o foco também ao vídeo (melhoria futura, fora do MVP 2.1).

---

# 13. Melhorias futuras (fora do escopo)

- **Validação editorial por IA**: 1 chamada de texto classificando se a curiosidade passa no perfil oddity (evita R-02); custo ~0 com modelos free.
- **Subcategorias mais "oddities"**: ajustar seeds (ex.: Animals → "Extinct Megafauna", "Unexplained Phenomena") — exige migration de HasData e refaz o balanceamento BR-007.
- **Foco no vídeo**: aplicar `imageFocus` ao prompt do `VideoGenerationStep` (MVP 2).
- **Validação automática da imagem**: checar se o sujeito está presente (visão multi-modal) — custo/complexidade altos hoje.
