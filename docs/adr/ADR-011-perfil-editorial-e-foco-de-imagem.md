# Architecture Decision Record - Perfil Editorial Oddity e Imagem Dirigida por Foco

## Status

Aceito

## Contexto

O MVP 2.1 nasceu de tres sintomas observados no conteudo publicado:

1. **Legendas curtas**: o limite de `MaxCaptionContentLength` era 800 caracteres, abaixo do que a legenda do Instagram suporta — posts ficavam menos substanciais do que precisavam.
2. **Temas triviais**: o prompt de texto pedia apenas "uma curiosidade factual" sem direcao editorial, e o resultado frequentemente eram fatos de superficie (ex.: a pata de uma ave) em vez de achados estranhos e profundos (ex.: as preguicas-gigantes de Peter Lund em Lagoa Santa possivelmente convivendo com humanos; o mecanismo de Antikythera).
3. **Imagens genericas**: a imagem era gerada a partir do `Theme` (um rotulo normalizado de ate 120 chars), entao nao havia garantia de que o assunto central da curiosidade aparecesse na imagem (texto sobre o mecanismo de Antikythera, imagem sem o mecanismo).

Restricoes: custo mensal inalterado (~USD 6), sem validacao editorial por IA (chamada extra) e sem regredir o comportamento atual em nenhum caminho.

## Decisao

1. **Legenda de ate 1600 caracteres**: `AppConfiguration.MaxCaptionContentLength` default 800 → 1600 (codigo + `appsettings*`). O prompt de texto e interpolado pela config pedindo a faixa `between {max/2} and {max}` em vez de um teto seco. Guarda nova: `PipelineConstants.InstagramMaxCaptionLength = 2200` valida a caption final completa (`TextContent + "\n\nSource: ..."`) e rejeita com `CAPTION_TOO_LONG` (fluxo de retry BR-006 identico ao `TEXT_TOO_LONG`). Seed `MAX_CAPTION_CONTENT_LENGTH` atualizado para 1600 (migration `UpdateMaxCaptionContentLengthSeed`).
2. **Brief editorial "oddity" hardcoded no adapter**: o system prompt do `OpenRouterTextGenerationAdapter` ganha um perfil editorial fixo em codigo (nao configuravel por env var — texto longo em env var e fragil; ajuste = deploy): incomum/misterioso, angulo mais obscuro da categoria, profundidade ancorada em lugar/pessoa/data/numero, exemplos de referencia, exclusao explicita de gore/decay/repulsivo e manutencao de BR-001 (factual + `sourceUrl` credivel). JSON schema e `response_format: json_object` inalterados. Sem validacao editorial por IA (melhoria futura).
3. **`imageFocus` gerado pelo modelo de texto**: o modelo de texto — que ja leu a curiosidade inteira — tambem gera `imageFocus` (campo opcional no JSON, max 300 chars): um sujeito visual concreto e especifico no centro da curiosidade, nao um clima vago. Enviar o texto integral ao modelo de imagem foi descartado em favor de um prompt curto, especifico e barato.
4. **Persistencia para auditoria**: `Post.ImageFocus` (VARCHAR(300), nullable, migration `AddPostImageFocus`) permite auditar/debugar imagens ruins depois (qual foco foi usado). O valor tambem flui por `PipelineContext.TextContext.ImageFocus`.
5. **Fallback `Theme` (nunca pior)**: `imageFocus` ausente/vazio no JSON → `Post.ImageFocus` e o prompt de imagem usam `Theme` → comportamento identico ao pre-MVP 2.1. O `OpenRouterImageGenerationAdapter` monta o prompt como `A poetic surreal illustration whose clear, unmistakable central subject is: {focus}. ...` mantendo a identidade "poetic surreal" da marca e acrescentando apenas a regra de foco (dominancia da composicao, detalhe concreto, sem texto/watermark).
6. **Estimativas de budget**: `EstimatedPromptTokens` 1500 → 2000 e `EstimatedCompletionTokens` 600 → 1200 (prompt maior + texto 2x maior), mantendo a estimativa pre-call honesta dentro de `MaxTextCostPerRequestUsd`.

Ordem de implementacao: RF-24 (tamanho) → RF-25 (editorial) → RF-26 (`imageFocus`) → RF-27 (imagem dirigida) → RF-28 (docs), cada um compilando e testando isoladamente.

## Consequencias

**Positivas**

- Legendas substanciais sem risco de erro `TEXT_TOO_LONG`/`CAPTION_TOO_LONG` em producao (retry BR-006 cobre as rejeicoes).
- Temas mais alinhados ao nicho do perfil, sem custo adicional (modelos free).
- Imagem passa a ter sujeito central identificavel, com `Post.ImageFocus` para auditoria.
- Fallback para `Theme` garante que nenhum caminho regride.

**Negativas**

- Modelo `:free` pode ignorar o brief editorial (R-02) — sem validacao automatica de conteudo; mitigacao manual pos-deploy.
- `imageFocus` pode ser vago/ausente no JSON — mitigado pelo fallback `Theme`.
- Brief duplica parcialmente a faixa de tamanho (schema + bullet `Length`) — aceito por clareza ao modelo.
- Seed divergente entre `main` (RF-24) e `feat/mvp2-planning` (MVP 2) apos merge futuro — resolver no merge aceitando 1600.

## Alternativas consideradas

- **Enviar o texto integral da curiosidade ao modelo de imagem**: rejeitado — prompt longo, custo/latencia maiores e o modelo de imagem pode se perder nos detalhes; um foco curto e mais direcionado.
- **`imageFocus` em env var/config**: rejeitado — o foco e por-curiosidade, nao global.
- **Validacao editorial por IA (chamada extra classificando o perfil oddity)**: adiada — evita R-02 mas adiciona chamada/custo; o dono pediu mudanca de prompt.
- **Coluna `text` para `Post.ImageFocus`**: rejeitada — 300 chars bastam para um sujeito visual; truncamento defensivo no step evita erro de insert.

## Relacionados

- [ADR-008 Selecao Dinamica de Modelos com Fallback e Budget](./ADR-008-modelo-dinamico-fallback-custo.md)
- [ADR-004 Clientes Separados](./ADR-004-openrouter-clientes-separados.md)
- Planejamento: [`../mvp2.1-planning.md`](../mvp2.1-planning.md)
