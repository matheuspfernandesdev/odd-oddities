# Documento de Arquitetura - Odd Oddities

> Documento de referencia arquitetural do projeto. Toda decisao registrada aqui foi confirmada durante o processo de discovery e arquitetura. Documentos relacionados: [`prd.md`](./prd.md), [`to-be-determined.md`](./to-be-determined.md) e [`adr/`](./adr/).

---

# Visao Geral do Produto

Odd Oddities e um perfil de Instagram que publica **tres curiosidades factuais por semana** em ingles, acompanhadas de ilustracoes artisticas geradas por IA. A automacao foi planejada como projeto pessoal de portfolio, com baixo custo operacional e alta simplicidade de manutencao.

O sistema e um **Worker .NET em Docker** que roda em uma VPS Contabo Cloud VPS 4 (Ubuntu LTS, 8 GB RAM, 100 GB SSD), consumindo OpenRouter para geracao de texto e imagem e a Instagram Graph API para publicacao. As imagens sao armazenadas em MinIO local, expostas por Nginx com TLS, e publicadas via URL pre-assinada.

# Objetivos de Negocio

- Publicar automaticamente tres posts por semana no Instagram.
- Experimentar diferentes modelos de IA via OpenRouter.
- Manter o custo total abaixo de US$ 10 por mes.
- Construir um projeto de portfolio limpo e bem documentado.
- Garantir manutencao por uma unica pessoa.

# Estrategia de Estado Atual e Migracao

Projeto **greenfield**. Nao existe sistema legado. Repositorio praticamente vazio, sem dados para migrar. Banco iniciara vazio e sera populado por seeds (categorias, subcategorias, configuracoes).

Nao ha fase de cutover, nao ha dual-write e nao ha Strangler Fig.

# Perfil de Equipe e Operacoes

- Um unico desenvolvedor.
- Experiencia avancada em .NET, Docker, PostgreSQL e APIs externas.
- Desenvolvedor sera tambem o operador e o suporte de producao.
- Nao ha equipe de DevOps, SRE ou QA.
- Equipe permanecera solo por 12 meses.
- Arquitetura deve priorizar simplicidade operacional.

# Modelo de Dominio

## Linguagem Ubiquua

- **Post**: uma publicacao (post + imagem) concluida ou em progresso.
- **Category**: classificacao macro do conteudo (Science, Nature, History, etc.).
- **Subcategory**: classificacao especifica dentro de uma Category (Ocean, Mammals, etc.).
- **Curiosity**: o texto-fonte gerado pela IA que da origem ao Post.
- **Summary**: resumo curto usado para deteccao de similaridade.
- **Theme**: rotulo normalizado usado na comparacao de similaridade.
- **GenerationAttempt**: tentativa de geracao registrada, mesmo quando rejeitada.
- **Publication**: registro da interacao com a Meta Graph API.
- **SystemSetting**: configuracao persistida em banco (chave/valor).
- **FailureStep**: etapa em que o pipeline falhou (enum).

## Diagrama textual

```text
Category
  │
  └── 1:N ── Subcategory
                │
                └── 1:N ── Post ─── 1:1 ─── Publication
                              │
                              └── 1:N ── GenerationAttempt

SystemSetting (entidade independente)
```

## Entidades principais

### Category

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | Chave primaria |
| Name | string(80) | sim | Nome da categoria |
| Description | string(500) | nao | Descricao |
| IsActive | bool | sim | Permite desativar sem excluir |
| CreatedAt | DateTime(UTC) | sim | Data de criacao |
| UpdatedAt | DateTime(UTC) | sim | Data de atualizacao |

### Subcategory

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | Chave primaria |
| CategoryId | long | sim | FK para Category |
| Name | string(80) | sim | Nome |
| Description | string(500) | nao | Descricao |
| IsActive | bool | sim | |
| CreatedAt | DateTime(UTC) | sim | |
| UpdatedAt | DateTime(UTC) | sim | |

### Post

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | PK |
| CategoryId | long | sim | FK |
| SubcategoryId | long | sim | FK |
| TextContent | text | sim | Curiosidade final |
| Summary | string(500) | sim | Resumo |
| Theme | string(120) | sim | Tema normalizado |
| ContentHash | string(64) | sim | Hash para duplicatas |
| SourceUrl | text | sim | URL da fonte |
| ImageObjectKey | string(255) | sim | Chave do objeto no MinIO |
| ImageWidth | int | sim | Largura processada |
| ImageHeight | int | sim | Altura processada |
| ImageBytes | bigint | sim | Tamanho final em bytes |
| Status | enum | sim | Generated, Validated, ImageProcessed, Published, Failed |
| FailureStep | enum | nao | Etapa da falha |
| FailureReason | text | nao | Descricao curta |
| ErrorCode | string(80) | nao | Codigo de erro |
| FailureDetails | text | nao | Detalhes sem segredos |
| Caption | text | sim | Texto final publicado |
| CreatedAt | DateTime(UTC) | sim | |
| UpdatedAt | DateTime(UTC) | sim | |
| PublishedAt | DateTime(UTC) | nao | |

### GenerationAttempt

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | PK |
| PostId | long | sim | FK para Post |
| AttemptNumber | int | sim | Sequencial |
| ModelId | string(120) | sim | Modelo usado |
| Status | enum | sim | Success, Rejected, Error |
| RejectionReason | string(255) | nao | |
| RawResponse | text | nao | Resposta original (sanitizada) |
| CostUsd | decimal(10,6) | nao | Custo registrado |
| TokensIn | int | nao | |
| TokensOut | int | nao | |
| DurationMs | bigint | sim | |
| CreatedAt | DateTime(UTC) | sim | |

### Publication

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | PK |
| PostId | long | sim | FK |
| MetaMediaId | string(120) | sim | Id retornado pela Meta |
| MetaMediaStatus | string(40) | sim | Ex: PUBLISHED, IN_PROGRESS |
| MetaMediaStatusCode | string(40) | sim | |
| MetaPermalink | text | nao | URL do post |
| AttemptCount | int | sim | Numero de tentativas |
| LastCheckedAt | DateTime(UTC) | sim | |
| CreatedAt | DateTime(UTC) | sim | |
| UpdatedAt | DateTime(UTC) | sim | |

### SystemSetting

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Key | string(80) | sim | PK |
| Value | text | sim | Valor (pode ser criptografado) |
| IsEncrypted | bool | sim | Define criptografia |
| Description | string(255) | nao | |
| UpdatedAt | DateTime(UTC) | sim | |

### PostAudit (auditoria detalhada)

| Atributo | Tipo | Obrigatorio | Descricao |
|---|---|---|---|
| Id | long | sim | PK |
| PostId | long | sim | FK |
| FieldName | string(80) | sim | |
| OldValue | text | nao | |
| NewValue | text | nao | |
| ChangedAt | DateTime(UTC) | sim | |

## Enums

### PostStatus

- Generated
- Validated
- ImageProcessed
- Published
- Failed

### FailureStep

- TextGeneration
- SourceValidation
- ImageGeneration
- ImageStorage
- Database
- InstagramApi

### AttemptStatus

- Success
- Rejected
- Error

## Eventos de Dominio

- PostGenerated
- PostValidated
- ImageProcessed
- PostPublished
- PostFailed

Eventos sao registrados por um publisher interno e logados no stdout. Nao ha event bus.

## Value Objects

- `SourceUrl`: URL HTTP/HTTPS bem formada, validada com `HEAD` ou `GET` limitado.
- `Summary`: texto nao vazio dentro de `MaxCaptionContentLength` (800 caracteres por padrao).
- `Theme`: rotulo normalizado, lowercase, sem acentos.
- `Caption`: curiosidade + `\n\nSource: <URL>`.
- `ImageObjectKey`: chave S3/MinIO (UUID + extensao).
- `ContentHash`: SHA-256 de `TextContent` normalizado.

# Regras de Negocio

| ID | Descricao | Escopo | Severidade | Mensagem |
|---|---|---|---|---|
| BR-001 | Conteudo deve ser factual, nao opinativo, nao ofensivo. | Pipeline | ERROR | Conteudo rejeitado por violar politica editorial. |
| BR-002 | `TextContent` nao pode exceder `MaxCaptionContentLength` (800). | Validacao | ERROR | Texto excedeu o limite permitido. |
| BR-003 | `SourceUrl` deve estar bem formada e responder 2xx/3xx. | Validacao | ERROR | URL da fonte invalida. |
| BR-004 | `ContentHash` nao pode existir em Post publicado nos ultimos 90 dias. | Validacao | ERROR | Conteudo duplicado. |
| BR-005 | Similaridade textual do `Summary` >= 80% rejeita o conteudo. | Validacao | ERROR | Conteudo muito semelhante a um post recente. |
| BR-006 | Ate 3 tentativas de geracao por execucao quando ha rejeicao. | Pipeline | WARNING | Limite de tentativas atingido. |
| BR-007 | Categoria e Subcategoria devem estar ativas. | Validacao | ERROR | Categoria ou subcategoria invalida. |
| BR-008 | Imagens sao processadas em 1080x1080 JPEG ~85 com marca d'agua. | Pipeline | ERROR | Falha no processamento de imagem. |
| BR-009 | MinIO quota de 20 GB; upload bloqueado quando atingida. | Pipeline | ERROR | Cota do MinIO atingida. |
| BR-010 | Token Meta deve ser renovado antes de 14 dias para expiracao. | Pipeline | ERROR | Falha na renovacao do token Meta. |
| BR-011 | Toda publicacao grava uma `Publication`. | Pipeline | ERROR | Falha na publicacao. |
| BR-012 | Toda publicacao deve publicar apenas uma imagem unica por execucao. | Pipeline | ERROR | Tipo de midia incompativel. |
| BR-013 | Quando publicado, status passa para Published e `PublishedAt` e preenchido. | Pipeline | ERROR | Status nao atualizou. |
| BR-014 | Posts e imagens nunca sao excluidos. | Politica | ERROR | Exclusao nao permitida. |

# Arquitetura

## Estilo

- **Decomposicao:** Monolito modular em Docker Compose.
- **Organizacao interna:** Hexagonal / Ports and Adapters.
- **Comunicacao:** Chamadas sincronas diretas dentro do Worker.

## Portas (dominio)

- `ITextGenerationPort` - OpenRouter (texto).
- `IImageGenerationPort` - OpenRouter (imagem).
- `IInstagramPublishingPort` - Meta Graph API.
- `IObjectStoragePort` - MinIO.
- `IPostRepository` - PostgreSQL.
- `IClock` - Relogio com timezone.

## Adapters (infraestrutura)

- `OpenRouterTextAdapter` usa `POST /api/v1/chat/completions`.
- `OpenRouterImageAdapter` usa `POST /api/v1/images`.
- `InstagramPublishingAdapter` usa endpoints da Meta Graph API.
- `MinioObjectStorageAdapter` usa SDK compativel com S3.
- `PostgresPostRepository` usa EF Core + Npgsql.
- `SystemClock` usa `TimeProvider` integrado ao .NET 8.

## Fluxo principal

```text
Cron (PeriodicTimer)
  |
  v
1. Selecionar Category e Subcategory menos usadas (90 dias)
2. Post.Created (status=Generated)
3. OpenRouterTextAdapter: gerar curiosidade (JSON)
4. Validar SourceUrl (HEAD)
5. Validar tamanho e similaridade textual
6. Post.Updated (status=Validated)
7. OpenRouterImageAdapter: gerar imagem (b64)
8. ImageSharp: redimensionar, marca d'agua, JPEG ~85
9. MinioObjectStorageAdapter: PutObject (chave UUID)
10. Verificar quota MinIO (20 GB)
11. Gerar URL pre-assinada (24h)
12. InstagramPublishingAdapter: criar container de midia
13. InstagramPublishingAdapter: publicar midia
14. Polling do status ate Published/Error
15. Persistir Publication
16. Post.Updated (status=Published, PublishedAt=now)
```

Em qualquer falha, o `Post` e marcado como `Failed` com `FailureStep`, `FailureReason`, `ErrorCode` e `FailureDetails` (sem segredos).

# Decisoes Arquiteturais (ADR)

- [ADR-001 Monolito Modular em Docker Compose](./adr/ADR-001-monolito-modular.md)
- [ADR-002 Hexagonal (Ports and Adapters)](./adr/ADR-002-hexagonal.md)
- [ADR-003 PostgreSQL como Banco de Dados](./adr/ADR-003-postgresql.md)
- [ADR-004 Clientes Separados para Texto e Imagem no OpenRouter](./adr/ADR-004-openrouter-clientes-separados.md)
- [ADR-005 MinIO Privado com Nginx HTTPS](./adr/ADR-005-metadados-armazenamento-minio.md)
- [ADR-006 Token Meta Renovado Criptografado no PostgreSQL](./adr/ADR-006-token-criptografado.md)
- [ADR-007 Retry com Backoff Exponencial](./adr/ADR-007-retry-backoff.md)

# Stack Tecnologica

## Backend

- .NET 8 (C# 12)
- `Microsoft.Extensions.Hosting` para Worker Service.
- `System.Threading.PeriodicTimer` para scheduler.
- `Npgsql.EntityFrameworkCore.PostgreSQL` para EF Core.
- `SixLabors.ImageSharp` para processamento de imagem.
- `AWSSDK.S3` (compativel com MinIO) ou `Minio` SDK.
- `Polly` para retry com backoff.
- `Serilog` para logs estruturados em JSON.

## Banco de Dados

- PostgreSQL 16.
- EF Core Migrations 8.x.

## Infraestrutura

- Docker 24+.
- Docker Compose v2.
- Nginx 1.24+.
- Certbot via container dedicado.
- Let's Encrypt.

## APIs externas

- OpenRouter (REST, HTTPS).
- Meta Instagram Graph API (REST, HTTPS).

## Ferramentas

- `xUnit` + `FluentAssertions` + `NSubstitute` para testes.
- `dotnet format` para formatacao.
- `dotnet build` e `dotnet test` no CI.

## Runtime e versoes

- .NET 8.0 (LTS).
- PostgreSQL 16.x.
- Ubuntu Server 22.04 LTS ou 24.04 LTS.

# Mapa de Integracoes Externas

| Provedor | Tipo | Autenticacao | SLA | Resiliencia |
|---|---|---|---|---|
| OpenRouter (texto) | REST, HTTPS | API key em header | Nao documentado | Retry 3x, backoff exponencial, log estruturado |
| OpenRouter (imagem) | REST, HTTPS | API key em header | Nao documentado | Retry 3x, backoff exponencial, log estruturado |
| Meta Graph API | REST, HTTPS | Bearer token (long-lived) | Nao documentado | Retry 3x, backoff exponencial, polling de status |
| MinIO | S3 API compativel | AccessKey/SecretKey interna | Auto-gerido | Sem retry para erros estruturais |

A primeira versao nao usa webhooks, SFTP, EDI ou pagamentos.

# Infraestrutura

- VPS Contabo Cloud VPS 4 (Ubuntu LTS, 8 GB RAM, 100 GB SSD).
- 40 GB livres no momento da POC.
- 20 GB reservados para o bucket MinIO.
- Containers via Docker Compose:
  - `worker` (aplicacao).
  - `postgres`.
  - `minio`.
  - `nginx`.
  - `certbot`.
- Redes:
  - `internal` (worker, postgres, minio).
  - `edge` (nginx, com porta 443 exposta).
- Dominio publico: `storage.<dominio>` apontando para a VPS.
- Certificados via Let's Encrypt, renovados periodicamente.
- DNS: registro A apontando para o IP da VPS.

# Estrategia de Banco de Dados

## Tecnologia

- PostgreSQL 16.

## Modelagem

- Tabelas principais: `Categories`, `Subcategories`, `Posts`, `GenerationAttempts`, `Publications`, `SystemSettings`, `PostAudits`.

## Indices

- PKs e FKs.
- Indice composto `(Status, CreatedAt)` para dashboards simples.
- Indice `(CategoryId, SubcategoryId, PublishedAt)` para balanceamento.
- Indice `(ContentHash)` para duplicatas.
- Indice em `Posts.PublishedAt` para janelas de 90 dias.

## Auditoria

- `CreatedAt` e `UpdatedAt` em todas as entidades.
- Tabela `PostAudits` registra alteracoes em `Post` e `Publication`.

## Soft delete

- Nao ha `DeletedAt`. Posts e imagens nunca sao removidos.
- Categorias/Subcategorias podem ser desativadas via `IsActive`.

## Migrations

- EF Core Migrations, aplicadas automaticamente no startup do Worker.

## Sem multi-tenancy

- Banco dedicado ao Odd Oddities.

# Padroes de API

A primeira versao **nao expoe API propria**. O Worker e consumidor de APIs externas.

## OpenRouter - Texto

- **Endpoint:** `POST https://openrouter.ai/api/v1/chat/completions`
- **Headers:**
  - `Authorization: Bearer <OPENROUTER_API_KEY>`
  - `Content-Type: application/json`
- **Body exemplo:**

```json
{
  "model": "google/gemma-4-26b-a4b-it:free",
  "messages": [
    { "role": "system", "content": "You generate factual curiosities..." },
    { "role": "user", "content": "Generate one curiosity." }
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

- **Resposta esperada:** `choices[0].message.content` em JSON.

## OpenRouter - Imagem

- **Endpoint:** `POST https://openrouter.ai/api/v1/images`
- **Headers:** mesmo Authorization.
- **Body exemplo:**

```json
{
  "model": "meta/muse-image",
  "prompt": "A poetic surreal illustration about..."
}
```

- **Resposta esperada:** `data[0].b64_json`.

## Meta Graph API

- **Upload:** `POST /v17.0/{ig-user-id}/media` com `image_url` e `caption`.
- **Publicacao:** `POST /v17.0/{ig-user-id}/media_publish` com `creation_id`.
- **Renovacao:** `GET https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token=<token>`.

## MinIO (S3 compativel)

- Operacoes: `PutObject`, `GetObject`, `PresignedGetObject`, `SetBucketQuota`.

# Seguranca

## Segredos

- `OPENROUTER_API_KEY` em GitHub Actions Secrets.
- `META_ACCESS_TOKEN`, `META_APP_ID`, `META_APP_SECRET` em GitHub Actions Secrets.
- `TOKEN_ENCRYPTION_KEY` em GitHub Actions Secrets.
- `POSTGRES_PASSWORD`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY` em GitHub Actions Secrets.
- `INSTAGRAM_USER_ID` em GitHub Actions Secrets.
- `MINIO_PUBLIC_ENDPOINT` em GitHub Actions Secrets.
- Variaveis nao sensiveis em GitHub Actions Variables:
  - `TEXT_MODEL_ID`
  - `IMAGE_MODEL_ID`
  - `MAX_CAPTION_CONTENT_LENGTH`
  - `SIMILARITY_THRESHOLD`
  - `SCHEDULE_HOUR_UTC`
  - `SCHEDULE_TIMEZONE`
  - `MINIO_BUCKET_NAME`
  - `MINIO_QUOTA_BYTES`
  - `POSTGRES_HOST`
  - `POSTGRES_PORT`
  - `POSTGRES_DB`
  - `POSTGRES_USER`

## Criptografia

- Token Meta persistido com AES-256-GCM no PostgreSQL.
- Chave mestra em variavel de ambiente, nunca no banco.
- Rotacao da chave apenas em caso de comprometimento.

## Transporte

- HTTPS obrigatorio para OpenRouter e Meta.
- MinIO acessado internamente pela rede Docker.
- Nginx exposto publicamente so para download da Meta.

## OWASP

- Validacao de entrada em todas as portas.
- Sanitizacao de logs (sem tokens, URLs assinadas, credenciais).
- Provider redator para campos sensiveis.

## LGPD/GDPR

- Nao ha dados pessoais de usuarios finais.
- Aplicam-se apenas boas praticas basicas.

# Privacidade

- Sem coleta de dados pessoais.
- Posts publicos sao unicos dados gerados.
- Logs nao devem conter PII.

# DevOps

## Branches

- `main` unica, com merges diretos.

## CI

- GitHub Actions em push na `main`.
- Jobs:
  - `build` (dotnet build).
  - `test` (dotnet test).
  - `docker` (build e push da imagem para GHCR com tag `latest` e SemVer).
  - `deploy` (SSH + docker compose pull/up).

## Deploy

- Imagem publicada em `ghcr.io/<org>/odd-oddities-worker`.
- SSH via chave `deploy_key` armazenada em GitHub Actions Secrets.
- `docker compose pull && docker compose up -d` na VPS.

## Versionamento

- `latest` + `YYYY.MM.DD.HHMMSS-sha`.

## Renovacao de certificado

- Container `certbot` dedicado.
- Job agendado no GitHub Actions chama `docker compose run --rm certbot renew`.

## Migrations

- Aplicadas automaticamente no startup do Worker.

# Observabilidade

- Logs estruturados em JSON via Serilog, enviados para stdout.
- Captura pelo Docker, retencao de 30 dias via rotacao nativa.
- Sem agregador externo.
- Middleware global captura excecoes nao tratadas.
- Metricas em cada log: `executionId`, `step`, `outcome`, `durationMs`, `costUsd`, `tokensIn`, `tokensOut`, `minioBytesUsed`, `postStatus`.
- Sem health check na primeira versao.

# Performance

- Cache em memoria de `Categories` e `Subcategories`, recarregado a cada 24 horas.
- Pool de conexoes Npgsql (`Maximum Pool Size=20`, `Minimum Pool Size=2`).
- Imagens convertidas com ImageSharp para `1080x1080` JPEG qualidade ~85.
- Marca d'agua discreta no canto inferior direito.
- Sem compressao adicional.

# Escalabilidade

- Worker stateless.
- Trava local em memoria com `SemaphoreSlim(1,1)` impede execucao paralela.
- Sem auto-scaling.
- Upgrade vertical da VPS reativo.

# Estrategia de Testes

- Testes unitarios com xUnit + FluentAssertions + NSubstitute.
- Cobertura minima recomendada: 70% no dominio.
- Sem testes de integracao ou E2E na POC.
- CI executa `dotnet test` em todo push na `main`.

# Internacionalizacao

- Conteudo publicado em ingles.
- Mensagens internas e logs em ingles.
- i18n/L10n declarado **nao aplicavel** na primeira versao.

# Documentacao

- `README.md` na raiz.
- `docs/architecture.md` (este arquivo).
- `docs/prd.md`.
- `docs/to-be-determined.md`.
- `docs/adr/`.
- `docs/nginx.md` (tutorial Nginx + Let's Encrypt).
- `docs/instagram-api.md` (tutorial Meta Graph API).
- `docs/openrouter.md` (tutorial OpenRouter).
- Documentacao atualizada no mesmo PR em que decisoes mudam.

# Registro de Riscos

| ID | Categoria | Descricao | Impacto | Probabilidade | Mitigacao |
|---|---|---|---|---|---|
| R1 | Tecnico | Indisponibilidade do Meta Muse Image | Alto | Media | ModelId configuravel, adapter trocavel, logs com modelo usado |
| R2 | Tecnico | Breaking changes na Instagram Graph API | Alto | Media | Logs com versao da API, middleware global, testes manuais |
| R3 | Tecnico | Token Meta expirado sem renovacao | Alto | Baixa | Rotina periodica, logs de expiracao, reautorizacao manual documentada |
| R4 | Tecnico | Conteudo inadequado ou impreciso | Medio | Media | Prompt com regras, validacao de tamanho e URL, auditoria |
| R5 | Operacional | VPS Contabo indisponivel | Alto | Baixa | Sem SLA, aceitacao de perda parcial |
| R6 | Operacional | Disco cheio | Medio | Baixa | Verificacao de quota antes do upload |
| R7 | Operacional | Processo zumbi no Worker | Medio | Baixa | Middleware global, lock, restart via Docker |
| R8 | Seguranca | Vazamento de tokens em logs | Alto | Baixa | Provider redator, revisao em PR |
| R9 | Seguranca | Chave AES comprometida | Alto | Baixa | Rotacao manual, recriptografia de tokens |
| R10 | Seguranca | SSRF no MinIO publico | Medio | Baixa | Bloqueio de IPs internos, URL pre-assinada |
| R11 | Infraestrutura | Certbot indisponivel na renovacao | Alto | Baixa | Job agendado, log de sucesso/falha |
| R12 | Integracao | OpenRouter fora do ar | Alto | Baixa | Retry com backoff, logs estruturados |
| R13 | Integracao | Meta fora do ar ou rate-limit | Medio | Baixa | Retry com backoff para 429/5xx |

# Analise de Custos

| Item | Custo estimado | Observacao |
|---|---|---|
| VPS Contabo Cloud VPS 4 | EUR 4,50 / mes | Plano atual |
| Dominio | ~USD 1 / mes | Ja existente |
| OpenRouter imagem | USD 0,12 / mes | 12 posts x USD 0,01 |
| OpenRouter texto | USD 0,00 / mes | Modelos gratuitos |
| Meta Graph API | USD 0,00 / mes | Plano gratuito |
| PostgreSQL | USD 0,00 / mes | Local |
| MinIO | USD 0,00 / mes | Local |
| Let's Encrypt | USD 0,00 / mes | |
| GitHub Actions | USD 0,00 / mes | Plano gratuito |
| GHCR | USD 0,00 / mes | |
| **Total estimado** | **~USD 6 / mes** | |

# Melhorias Futuras

- Adicionar health check HTTP interno.
- Adicionar alerta simples em log para expiracao do token Meta.
- Implementar fallback automatico para modelos alternativos de imagem.
- Painel administrativo via Blazor ou React para visualizacao.
- Suporte a carrossel.
- Suporte a Reels.
- i18n/L10n multi-idioma.
- Backups automaticos.

# Perguntas em Aberto

Veja [`to-be-determined.md`](./to-be-determined.md).

# Recomendacoes Finais

- Manter a disciplina de atualizar documentacao em todo PR.
- Monitorar `modelId` e `costUsd` para ajustar stack de IA.
- Validar fluxo de renovacao do token Meta antes do primeiro deploy.
- Testar manualmente o pipeline end-to-end no primeiro deploy.

# Go-Live Readiness Report

- [x] Procedimento de rollback definido (imagens anteriores no GHCR; politica formal nao obrigatoria).
- [ ] Runbook de incidentes (a ser gerado antes do go-live).
- [x] Monitoramento validado em producao-like (logs estruturados).
- [x] Responsabilidade on-call atribuida (voce).
- [x] Pipeline de deploy com rollback disponivel (imagens antigas).
- [ ] Backup do banco verificado (sem backup formal nesta versao).
- [x] Revisao de seguranca concluida (este planejamento).
- [ ] Teste de carga executado (nao aplicavel nesta POC).
- [ ] Integracoes testadas em producao-like (validacao no primeiro deploy).
- [x] Controle de acesso revisado (somente voce).
