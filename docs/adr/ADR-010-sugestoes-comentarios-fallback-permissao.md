# Architecture Decision Record - Sugestoes de Tema via Comentarios com Fallback de Permissao

## Status

Aceito

## Contexto

O MVP 2 adicionou a leitura de comentarios dos ultimos posts para transformar sugestoes de tema em post do dia, com credito ao autor (`Suggested by @user`). A leitura usa `GET /v26.0/{ig-media-id}/comments` e a resposta usa `POST /v26.0/{comment-id}/replies`, ambos em `graph.instagram.com` (mesmo dominio ja usado).

Duas restricoes definiram o desenho:

1. **Permissao ausente (risco R14, quase certo)**: ler e responder comentarios exige `instagram_business_manage_comments`. O token atual foi gerado com `instagram_business_basic` + `instagram_business_content_publish` e **nao carrega essa permissao**. Sem ela, a Meta responde erro (codes 10/190/3, "Application does not have permission"). Nao podiamos deixar o pipeline falhar por causa disso.
2. **Ordem no pipeline vs. dado necessario (correcao 8.1 item 4)**: o desenho original colocava o `CommentSuggestionStep` como primeiro step e validava hash/similaridade ali — mas hash/similaridade exigem o texto ja gerado, que so existe depois do `TextGenerationStep`. Um Post ainda nao existe no inicio do pipeline.

## Decisao

1. **Novo step `CommentSuggestionStep` como primeiro do pipeline**: le os ultimos `Comments.LookbackPosts` (15) posts publicados com `MetaMediaId`, coleta comentarios novos (idempotencia por `CommentId` unico na tabela `CommentSuggestions`), e chama a cadeia de texto (modelos free do ADR-008, 1 chamada) para classificar cada comentario: e sugestao de tema factual adequado ao perfil? extrai `{theme, summary}`.
2. **Feature flag default OFF**: `AppConfiguration:Comments:Enabled` default `false`. Com flag off, o step nem chama a API (`StepResult.Skipped()` com log). So liga apos regenerar o token com a permissao (runbook em `instagram-api.md`).
3. **Fallback de permissao (risco R14)**: erro de permissao Meta (HTTP 403 ou body com codes 10/190/3 na `HttpRequestException` embutida por `EnsureSuccessAsync`) → log warning `comment permission missing — skipping comment suggestion step`, step `Skipped`, pipeline segue normal (gera post por categoria). Nenhuma execucao falha por falta da permissao. Outros erros Meta tambem viram `Skipped` (leitura de comentario nunca derruba o dia).
4. **Validacao editorial dentro do `TextGenerationStep`, nao no step de comentario (correcao 8.1 item 4)**: o `CommentSuggestionStep` apenas classifica e preenche `context.Suggestion`. A validacao de hash/similaridade (BR-004/BR-005) acontece dentro do `TextGenerationStep`, depois que o texto do tema sugerido e gerado. Se rejeitado, marca `CommentSuggestion.Rejected` com o motivo, limpa `context.Suggestion` e **re-tenta pelo fluxo normal de categoria** — a execucao sempre produz post, sem consumir tentativas extras de BR-006.
5. **No maximo 1 sugestao aceita por execucao**: so a primeira sugestao valida vira `Accepted`; demais sugestoes recebem `Classification = NotSuggestion` com `RejectionReason = "LIMIT_ONE_SUGGESTION_PER_RUN"` (auditoria honesta + idempotencia). Todo comentario novo e persistido (`NotSuggestion`/`Rejected`/`Accepted`) e nunca reprocessado.
6. **Credito e reply**: `TextGenerationStep` injeta `Suggested by @{author}` na caption e liga `Post.SourceCommentSuggestionId`. Apos publicar com sucesso, o `PublicationStep` faz `ReplyToCommentAsync(commentId, "Thanks for the suggestion!")` fire-and-forget (timeout proprio de 10 s); falha de reply so gera warning e nunca falha o Post — o credito ja esta na legenda.
7. **Nao roda em dia de video**: `CommentSuggestionStep` skipa quando `context.IsVideoRun` (a execucao de video usa legenda padrao, nao sugestao).

## Consequencias

**Positivas**

- Feature entregue com o token atual sem quebrar nada: com flag off ou sem permissao, o pipeline e identico ao MVP 1.
- Idempotencia forte por `CommentId` unico — comentario nunca processado 2x.
- A execucao sempre produz um post (sugestao ou curiosidade), preservando a cadencia de 3/semana.
- Auditoria completa em `CommentSuggestions` (inclusive rejeicoes e limite por execucao).

**Negativas**

- A feature so funciona de fato apos passo manual de regeneracao de token (runbook) — trabalho operacional fora do deploy.
- Classificacao por IA pode aceitar spam/troll como sugestao (mitigado por validacao editorial, similaridade e limite de 1/execucao — risco R19).
- Polling por API em vez de webhooks; adequado para o volume (~15 GET/execucao), mas nao escala para altissimo volume (webhooks anotado como melhoria futura).

## Alternativas consideradas

- **Webhooks `comments`**: rejeitada no MVP — exigiria endpoint publico de webhook na VPS, verificacao de assinatura e validacao do app na Meta. Polling e suficiente para 15 posts a cada 3 dias. Anotado como melhoria futura.
- **Validar hash/similaridade dentro do `CommentSuggestionStep`**: rejeitada — nao ha texto para hashar antes do `TextGenerationStep`; a validacao editorial foi movida para onde o texto existe (correcao 8.1 item 4).
- **Falhar o pipeline quando a permissao falta**: rejeitada — quebraria a cadencia de posts por um motivo operacional; o fallback silencioso com warning e flag off e mais seguro.
- **Aceitar multiplas sugestoes por execucao**: rejeitada — uma execucao publica uma midia; multiplas sugestoes aceitas nao teriam onde ir. Limite de 1 com auditoria das demais.

## Relacionados

- [ADR-008 Selecao Dinamica de Modelos com Fallback e Budget](./ADR-008-modelo-dinamico-fallback-custo.md)
- [ADR-009 Pipeline Alternativo de Video com Steps Skip-able](./ADR-009-pipeline-video-skip-able.md)
- [ADR-006 Token Meta Renovado Criptografado no PostgreSQL](./ADR-006-token-criptografado.md)
