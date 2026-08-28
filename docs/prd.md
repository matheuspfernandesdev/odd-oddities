# Product Requirements Document - Odd Oddities

> Documento fonte unico para o Coding Agent. Auto-contido, prescritivo e detalhado. Documentos relacionados: [`architecture.md`](./architecture.md), [`to-be-determined.md`](./to-be-determined.md).

---

# Visao Geral do Produto

## Proposito

Odd Oddities e um Worker de automacao que publica **tres curiosidades factuais por semana** no perfil de Instagram Odd Oddities, em ingles, com imagem artistica gerada por IA. O sistema foi desenhado como projeto pessoal de portfolio, com baixo custo operacional, execucao totalmente automatica em Docker e manutencao por uma unica pessoa.

## Publico-alvo

Pessoas que gostam de curiosidades, fatos incomuns e conteudo visual artistico no Instagram. Nao ha segmentacao por idade, regiao ou idioma na primeira versao.

## Proposta de valor

- Conteudo consistente: tres publicacoes semanais sem necessidade de intervencao manual.
- Identidade visual propria: ilustracoes surrealistas/poeticas com marca d'agua discreta.
- Custo baixo: menos de USD 10 por mes considerando IA, VPS e dominio.
- Manutencao simples: configuracao via variaveis de ambiente e seeds de banco.

## Stakeholders

- **Dono do produto**: voce, responsavel por tudo (desenvolvimento, deploy, suporte, conteudo editorial indireto).

## Links uteis

- Repositorio: `<github>/odd-oddities`
- Documentacao Meta: https://developers.facebook.com/docs/instagram-platform
- Documentacao OpenRouter: https://openrouter.ai/docs
- Imagem da logo: `assets/logo-watermark.png`
- Documento de ideia original: `docs/the-idea.md`

---

# Glossario / Linguagem Ubiquua

| Termo | Definicao |
|---|---|
| Post | Publicacao completa registrada no banco, com imagem e legenda |
| Curiosity | Texto-fonte gerado pela IA, antes de virar Post |
| Summary | Resumo curto do conteudo, usado para similaridade |
| Theme | Rotulo normalizado usado para balanceamento e similaridade |
| Category | Classificacao macro do conteudo |
| Subcategory | Classificacao especifica dentro de uma Category |
| GenerationAttempt | Cada tentativa de geracao, mesmo quando rejeitada |
| Publication | Registro da interacao com a Meta Graph API |
| ContentHash | SHA-256 do texto normalizado, usado para detectar duplicatas |
| SourceUrl | URL da fonte da curiosidade |
| Caption | Texto final publicado no Instagram (curiosidade + Source) |
| FailureStep | Etapa em que o pipeline falhou |
| FailureReason | Descricao curta da falha |
| Worker | Container .NET que executa o pipeline |
| Adapter | Implementacao concreta de uma porta hexagonal |
| SystemSetting | Configuracao persistida em banco (chave/valor) |

**Sinonimos proibidos:**

- "Curiosidade gerada" deve ser chamada de `Curiosity`.
- "Post no Instagram" deve ser chamado de `Post` quando persistido, ou `publicacao` quando ja no Instagram.
- "Erro" em logs deve usar `outcome = Failed`.

**Abreviacoes:**

- ET = Eastern Time.
- ETD = Eastern Daylight Time.
- EST = Eastern Standard Time.
- IG = Instagram.
- API = Application Programming Interface.
- RPO = Recovery Point Objective.

---

# User Personas

## Persona 1: Curioso do Instagram

- **Nome ficticio**: Carlos, 28 anos.
- **Ocupacao**: profissional de TI, folheia Instagram nos horarios livres.
- **Objetivos**: ver conteudo rapido e visualmente interessante.
- **Dores**: posts longos, clickbait, conteudo repetitivo.
- **Criterio de sucesso**: encontrar algo novo e surpreendente em menos de 30 segundos.
- **Contexto de uso**: celular, durante intervalos do trabalho.

---

# Requisitos Funcionais

## [x] RF-01: Pipeline de publicacao automatica

**User Story:** Como dono do perfil, quero que o sistema publique tres posts por semana sem intervencao manual para manter consistencia do perfil.

**Criterios de aceitacao:**

1. O pipeline dispara automaticamente nos dias e horarios definidos.
2. Cada publicacao concluida registra um `Post` com `Status = Published`.
3. Em caso de falha, registra `Status = Failed` com `FailureStep` apropriado.
4. Apos falha, o sistema aguarda o proximo horario sem nova tentativa imediata.
5. Apenas uma execucao do pipeline ocorre por vez.

**Regras de negocio associadas:** BR-006, BR-013, BR-014.

**Fluxo principal:**

1. Scheduler dispara no horario configurado.
2. Seleciona Category e Subcategory com menor uso nos ultimos 90 dias.
3. Cria `Post` com `Status = Generated`.
4. Chama OpenRouter (texto) para gerar curiosidade estruturada.
5. Valida `SourceUrl`.
6. Valida tamanho, duplicata e similaridade.
7. Chama OpenRouter (imagem).
8. Processa imagem com ImageSharp.
9. Faz upload para MinIO.
10. Gera URL pre-assinada (24h).
11. Cria container de midia na Meta.
12. Publica a midia.
13. Consulta status ate Published/Error.
14. Persiste `Publication` e marca `Post` como `Published`.

**Fluxos alternativos:**

- Se a Subcategory escolhida nao existir, seleciona a proxima menos usada.

**Fluxos de excecao:**

- `TextGeneration` falha: registra `FailureStep = TextGeneration`.
- `SourceValidation` falha: registra `FailureStep = SourceValidation`.
- Similaridade ou duplicata rejeita: nova geracao (ate 3 tentativas).
- `ImageGeneration` falha: registra `FailureStep = ImageGeneration`.
- Quota MinIO excedida: registra `FailureStep = ImageStorage`.
- Falha no upload Meta: registra `FailureStep = InstagramApi`.
- Polling retorna erro: registra `FailureStep = InstagramApi`.

**Campos e validacoes:**

| Campo | Tipo | Obrigatorio | Validacao |
|---|---|---|---|
| TextContent | text | sim | <= 800 caracteres |
| Summary | string(500) | sim | Nao vazio |
| Theme | string(120) | sim | Normalizado |
| SourceUrl | text | sim | URL HTTP/HTTPS valida |
| CategoryName | string(80) | sim | Deve existir em Categories |
| SubcategoryName | string(80) | sim | Deve existir em Subcategories da Category |

**Permissoes:** nenhuma (sistema autonomo).

**Dependencias:** nenhuma.

---

## [x] RF-02: Agendamento de execucoes

**User Story:** Como dono, quero definir dias e horarios fixos para as publicacoes para atingir o publico no momento certo.

**Criterios de aceitacao:**

1. As publicacoes ocorrem em terca, quinta e sabado.
2. Horario base: 17:00 UTC.
3. Conversao automatica para Eastern Time (com Daylight Saving).
4. Nenhuma execucao paralela.

**Regras de negocio:** BR-006.

**Campos e validacoes:**

| Campo | Tipo | Obrigatorio | Validacao |
|---|---|---|---|
| SCHEDULE_HOUR_UTC | int | sim | 0-23 |
| SCHEDULE_TIMEZONE | string | sim | IANA ou Windows time zone |
| SCHEDULE_DAYS | string | sim | Lista de dias (TUE, THU, SAT) |

**Dependencias:** nenhuma.

---

## [x] RF-03: Renovacao automatica do token Meta

**User Story:** Como dono, quero que o token de longa duracao da Meta seja renovado automaticamente para evitar expiracao.

**Criterios de aceitacao:**

1. A cada execucao, o sistema verifica a data de expiracao do token.
2. Quando faltar menos de 14 dias, dispara `refresh_access_token`.
3. O novo token e criptografado e substitui o anterior.
4. Em caso de falha, registra `FailureStep = InstagramApi`.

**Regras de negocio:** BR-010.

**Campos e validacoes:**

| Campo | Tipo | Obrigatorio | Validacao |
|---|---|---|---|
| MetaAccessToken | text | sim | Criptografado AES-256-GCM |
| MetaTokenExpiresAt | DateTime(UTC) | sim | Futuro |

**Dependencias:** Meta Graph API.

---

## [x] RF-04: Armazenamento permanente de imagens

**User Story:** Como dono, quero manter todas as imagens permanentemente no MinIO para preservar o acervo.

**Criterios de aceitacao:**

1. Cada imagem processada e salva no MinIO com chave UUID.
2. Imagens tem versao JPEG 1080x1080.
3. Bucket tem quota de 20 GB.
4. Quando a quota e atingida, uploads sao bloqueados.

**Regras de negocio:** BR-008, BR-009.

**Campos e validacoes:**

| Campo | Tipo | Obrigatorio | Validacao |
|---|---|---|---|
| ImageObjectKey | string(255) | sim | UUID |
| ImageBytes | bigint | sim | > 0 |
| ImageWidth | int | sim | 1080 |
| ImageHeight | int | sim | 1080 |

**Dependencias:** MinIO.

---

## [x] RF-05: Publicacao com URL pre-assinada

**User Story:** Como dono, quero que as URLs geradas para a Meta expirem em 24 horas para reduzir superficie de ataque.

**Criterios de aceitacao:**

1. URL pre-assinada e gerada antes do envio a Meta.
2. Validade: 24 horas.
3. URL aponta para o dominio publico HTTPS configurado.
4. Apos a publicacao, a URL pode expirar; o objeto permanece no MinIO.

**Regras de negocio:** nenhuma especifica.

**Dependencias:** MinIO, Nginx, Let's Encrypt.

---

## [x] RF-06: Selecao equilibrada de categorias

**User Story:** Como dono, quero que o sistema escolha categorias e subcategorias menos usadas para garantir variedade.

**Criterios de aceitacao:**

1. Sistema identifica a Category menos usada nos ultimos 90 dias.
2. Dentro dela, identifica a Subcategory menos usada.
3. Empate e resolvido por ordem alfabetica.
4. Se a Subcategory escolhida for invalida, refaz a busca.

**Regras de negocio:** BR-007.

**Dependencias:** banco PostgreSQL.

---

## [x] RF-07: Validacao de similaridade textual

**User Story:** Como dono, quero evitar repeticoes de temas nos ultimos 90 dias para manter variedade.

**Criterios de aceitacao:**

1. O sistema calcula o `ContentHash` do novo conteudo.
2. Se o hash ja existir em Post publicado nos ultimos 90 dias, rejeita.
3. Se a similaridade textual do `Summary` >= 80% com algum `Summary` dos ultimos 90 dias, rejeita.
4. Apos 3 rejeicoes consecutivas, marca o Post como `Failed` com `FailureStep = TextGeneration`.

**Regras de negocio:** BR-004, BR-005, BR-006.

**Dependencias:** algoritmo de similaridade textual (Jaccard sobre tokens normalizados).

---

## [x] RF-08: Validacao da URL de fonte

**User Story:** Como dono, quero garantir que a URL da fonte seja valida antes de publicar.

**Criterios de aceitacao:**

1. A URL deve estar bem formada (HTTP/HTTPS).
2. Uma chamada `HEAD` (ou `GET` limitado) deve retornar 2xx ou 3xx.
3. Timeout: 10 segundos.
4. Bloqueio de IPs internos (RFC1918, localhost, link-local).
5. Maximo de 3 redirecionamentos.

**Regras de negocio:** BR-003.

**Dependencias:** nenhuma.

---

## [x] RF-09: Processamento de imagem

**User Story:** Como dono, quero imagens em 1080x1080 com marca d'agua discreta para manter identidade visual.

**Criterios de aceitacao:**

1. Imagem retornada pela IA (PNG/Base64) e decodificada.
2. Redimensionada para 1080x1080 mantendo aspect ratio com crop central.
3. Marca d'agua "Odd Oddities" discreta, branca, canto inferior direito.
4. Salva em JPEG qualidade ~85.
5. Tamanho final medio esperado: 150-300 KB.

**Regras de negocio:** BR-008.

**Dependencias:** ImageSharp.

---

## [x] RF-10: Logs estruturados

**User Story:** Como dono, quero logs estruturados em JSON para facilitar analise e troubleshooting.

**Criterios de aceitacao:**

1. Logs vao para stdout.
2. Capturados pelo Docker.
3. Rotacao nativa, retencao de 30 dias.
4. Campos sensiveis sempre ofuscados.
5. Cada log inclui `executionId`, `step`, `outcome`, `durationMs`.

**Regras de negocio:** nenhuma.

**Dependencias:** Serilog.

---

## [x] RF-11: Tratamento global de erros

**User Story:** Como dono, quero que erros inesperados sejam capturados e registrados para evitar silenciamentos.

**Criterios de aceitacao:**

1. Middleware global captura qualquer excecao nao tratada.
2. Excecao e convertida em log estruturado.
3. Stack trace permanece apenas em log local.
4. Status do Post e atualizado para `Failed`.

**Regras de negocio:** nenhuma.

**Dependencias:** nenhuma.

---

## [x] RF-12: Migrations automaticas

**User Story:** Como dono, quero que as migrations do banco sejam aplicadas no startup do Worker para evitar passos manuais.

**Criterios de aceitacao:**

1. Worker executa `dotnet ef database update` antes do scheduler iniciar.
2. Falha na migration impede o inicio do scheduler.
3. Container fica unhealthy em caso de falha.

**Regras de negocio:** nenhuma.

**Dependencias:** EF Core.

---

# Requisitos Nao Funcionais

## Performance

- Tempo total da execucao completa: alvo < 90 segundos.
- Geracao de texto: alvo < 10 segundos.
- Validacao de URL: alvo < 10 segundos.
- Geracao de imagem: alvo < 60 segundos.
- Upload no MinIO: alvo < 5 segundos.
- Publicacao na Meta: alvo < 30 segundos (incluindo polling).

## Disponibilidade / SLA

- Sem SLA formal.
- Operacao em melhor esforco.
- Disponibilidade da VPS fora do controle do projeto.

## Concorrencia

- Apenas uma execucao do pipeline por vez (lock em memoria).
- Usuarios simultaneos: 1 (operador).

## Seguranca

- HTTPS obrigatorio para OpenRouter e Meta.
- MinIO acessado somente pela rede Docker interna.
- Console do MinIO nao exposto publicamente.
- Token Meta criptografado em repouso.
- Logs sanitizados, sem tokens, URLs assinadas, chaves.

## Compatibilidade

- Sem requisitos de browser ou dispositivo (nao ha UI).
- Compatibilidade da Meta API: usar a versao mais recente disponivel.

## Acessibilidade

- Nao aplicavel (sem interface web na primeira versao).

## Armazenamento

- ~12 imagens por mes, ~150-300 KB cada.
- Total estimado por ano: < 50 MB.
- MinIO quota: 20 GB (inclui margem para testes).

## Backup e Recovery

- Sem backup adicional.
- RPO: perda total aceita em caso de falha da VPS.
- Recovery: reinstalacao manual com seed de banco.

## Logging e Auditoria

- Logs retidos por 30 dias.
- Tabela `PostAudits` registra alteracoes em `Post` e `Publication`.

## Regulatorio / Compliance

- Sem LGPD formal (nao ha dados pessoais).
- Sem PCI, SOX ou HIPAA.

---

# Modelo de Dominio

## Diagrama simplificado

```text
Category
  |
  └── 1:N ── Subcategory
                |
                └── 1:N ── Post ─── 1:1 ─── Publication
                              |
                              └── 1:N ── GenerationAttempt

SystemSetting (entidade independente)
PostAudit (auditoria de Post)
```

## Entidades principais

(Detalhamento completo em `architecture.md`, secao "Modelo de Dominio".)

### Category

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| Name | string(80) | Nome |
| Description | string(500) | Descricao |
| IsActive | bool | |
| CreatedAt | DateTime(UTC) | |
| UpdatedAt | DateTime(UTC) | |

### Subcategory

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| CategoryId | long | FK |
| Name | string(80) | |
| Description | string(500) | |
| IsActive | bool | |
| CreatedAt | DateTime(UTC) | |
| UpdatedAt | DateTime(UTC) | |

### Post

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| CategoryId | long | FK |
| SubcategoryId | long | FK |
| TextContent | text | Curiosidade final |
| Summary | string(500) | Resumo |
| Theme | string(120) | Tema normalizado |
| ContentHash | string(64) | Hash SHA-256 |
| SourceUrl | text | URL fonte |
| ImageObjectKey | string(255) | Chave MinIO |
| ImageWidth | int | 1080 |
| ImageHeight | int | 1080 |
| ImageBytes | bigint | Tamanho final |
| Status | enum | Generated/Validated/ImageProcessed/Published/Failed |
| FailureStep | enum | |
| FailureReason | text | |
| ErrorCode | string(80) | |
| FailureDetails | text | |
| Caption | text | |
| CreatedAt | DateTime(UTC) | |
| UpdatedAt | DateTime(UTC) | |
| PublishedAt | DateTime(UTC) | |

### GenerationAttempt

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| PostId | long | FK |
| AttemptNumber | int | Sequencial |
| ModelId | string(120) | |
| Status | enum | Success/Rejected/Error |
| RejectionReason | string(255) | |
| RawResponse | text | |
| CostUsd | decimal(10,6) | |
| TokensIn | int | |
| TokensOut | int | |
| DurationMs | bigint | |
| CreatedAt | DateTime(UTC) | |

### Publication

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| PostId | long | FK |
| MetaMediaId | string(120) | |
| MetaMediaStatus | string(40) | |
| MetaMediaStatusCode | string(40) | |
| MetaPermalink | text | |
| AttemptCount | int | |
| LastCheckedAt | DateTime(UTC) | |
| CreatedAt | DateTime(UTC) | |
| UpdatedAt | DateTime(UTC) | |

### SystemSetting

| Atributo | Tipo | Descricao |
|---|---|---|
| Key | string(80) | PK |
| Value | text | |
| IsEncrypted | bool | |
| Description | string(255) | |
| UpdatedAt | DateTime(UTC) | |

### PostAudit

| Atributo | Tipo | Descricao |
|---|---|---|
| Id | long | PK |
| PostId | long | FK |
| FieldName | string(80) | |
| OldValue | text | |
| NewValue | text | |
| ChangedAt | DateTime(UTC) | |

## Enums

- PostStatus: Generated, Validated, ImageProcessed, Published, Failed.
- FailureStep: TextGeneration, SourceValidation, ImageGeneration, ImageStorage, Database, InstagramApi.
- AttemptStatus: Success, Rejected, Error.

## Value Objects

- SourceUrl, Summary, Theme, Caption, ImageObjectKey, ContentHash.

## Eventos de Dominio

- PostGenerated
- PostValidated
- ImageProcessed
- PostPublished
- PostFailed

Eventos sao logados no stdout, nao publicados em bus.

---

# Regras de Negocio

(Detalhamento em `architecture.md`.)

- BR-001: Conteudo factual, nao opinativo, nao ofensivo.
- BR-002: `TextContent` <= 800 caracteres.
- BR-003: `SourceUrl` valida e HTTP 2xx/3xx.
- BR-004: `ContentHash` nao duplicado em 90 dias.
- BR-005: Similaridade textual >= 80% rejeita.
- BR-006: Ate 3 tentativas por execucao.
- BR-007: Category e Subcategory ativas.
- BR-008: Imagem processada em 1080x1080 JPEG ~85 com marca d'agua.
- BR-009: MinIO quota 20 GB.
- BR-010: Token Meta renovado antes de 14 dias para expiracao.
- BR-011: Publicacao registrada.
- BR-012: Apenas uma imagem por execucao.
- BR-013: Post publicado atualiza `Status` e `PublishedAt`.
- BR-014: Posts e imagens nunca excluidos.

---

# Stack Tecnologica

- Backend: .NET 8 (C# 12).
- Banco: PostgreSQL 16.
- ORM: EF Core 8 com Npgsql.
- Imagens: SixLabors.ImageSharp.
- Storage: MinIO + MinIO .NET SDK ou AWSSDK.S3.
- HTTP: HttpClient nativo + Polly para retry.
- Logging: Serilog com sink Console.
- Scheduler: PeriodicTimer nativo do .NET.
- Testes: xUnit + FluentAssertions + NSubstitute.
- Formatacao: `dotnet format`.

## Versoes exatas

- .NET 8.0.x (LTS).
- PostgreSQL 16.x.
- MinIO RELEASE.2024-12.x ou mais recente.
- Nginx 1.24+.
- Certbot 2.x (no container).
- Ubuntu Server 22.04 LTS ou 24.04 LTS.

---

# Database Schema

## Categorias e Subcategorias

### Categories

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| Name | VARCHAR(80) | nao | | |
| Description | VARCHAR(500) | sim | | |
| IsActive | BOOLEAN | nao | TRUE | |
| CreatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| UpdatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

- UNIQUE (Name).
- INDEX (IsActive).

### Subcategories

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| CategoryId | BIGINT | nao | | FK -> Categories(Id) |
| Name | VARCHAR(80) | nao | | |
| Description | VARCHAR(500) | sim | | |
| IsActive | BOOLEAN | nao | TRUE | |
| CreatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| UpdatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

- UNIQUE (CategoryId, Name).
- INDEX (CategoryId, IsActive).

## Posts

### Posts

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| CategoryId | BIGINT | nao | | FK |
| SubcategoryId | BIGINT | nao | | FK |
| TextContent | TEXT | nao | | |
| Summary | VARCHAR(500) | nao | | |
| Theme | VARCHAR(120) | nao | | |
| ContentHash | VARCHAR(64) | nao | | |
| SourceUrl | TEXT | nao | | |
| ImageObjectKey | VARCHAR(255) | nao | | |
| ImageWidth | INT | nao | 1080 | |
| ImageHeight | INT | nao | 1080 | |
| ImageBytes | BIGINT | nao | | |
| Status | SMALLINT | nao | | |
| FailureStep | SMALLINT | sim | | |
| FailureReason | TEXT | sim | | |
| ErrorCode | VARCHAR(80) | sim | | |
| FailureDetails | TEXT | sim | | |
| Caption | TEXT | nao | | |
| CreatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| UpdatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| PublishedAt | TIMESTAMP WITH TIME ZONE | sim | | |

- INDEX (Status, CreatedAt).
- INDEX (CategoryId, SubcategoryId, PublishedAt).
- INDEX (ContentHash).
- INDEX (PublishedAt).
- INDEX (Theme).

### GenerationAttempts

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| PostId | BIGINT | nao | | FK |
| AttemptNumber | INT | nao | | |
| ModelId | VARCHAR(120) | nao | | |
| Status | SMALLINT | nao | | |
| RejectionReason | VARCHAR(255) | sim | | |
| RawResponse | TEXT | sim | | |
| CostUsd | DECIMAL(10,6) | sim | | |
| TokensIn | INT | sim | | |
| TokensOut | INT | sim | | |
| DurationMs | BIGINT | nao | | |
| CreatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

- INDEX (PostId, AttemptNumber).

### Publications

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| PostId | BIGINT | nao | | FK |
| MetaMediaId | VARCHAR(120) | nao | | |
| MetaMediaStatus | VARCHAR(40) | nao | | |
| MetaMediaStatusCode | VARCHAR(40) | nao | | |
| MetaPermalink | TEXT | sim | | |
| AttemptCount | INT | nao | 1 | |
| LastCheckedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| CreatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |
| UpdatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

- UNIQUE (PostId).
- INDEX (MetaMediaId).

### PostAudits

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Id | BIGSERIAL | nao | | PK |
| PostId | BIGINT | nao | | FK |
| FieldName | VARCHAR(80) | nao | | |
| OldValue | TEXT | sim | | |
| NewValue | TEXT | sim | | |
| ChangedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

- INDEX (PostId, ChangedAt).

### SystemSettings

| Coluna | Tipo SQL | Nullable | Default | PK/FK |
|---|---|---|---|---|
| Key | VARCHAR(80) | nao | | PK |
| Value | TEXT | nao | | |
| IsEncrypted | BOOLEAN | nao | FALSE | |
| Description | VARCHAR(255) | sim | | |
| UpdatedAt | TIMESTAMP WITH TIME ZONE | nao | NOW() | |

## Estrategia de soft delete

Nenhuma tabela tem `DeletedAt`. Posts e imagens nao sao removidos.

## Audit trail

- `CreatedAt` e `UpdatedAt` em todas as entidades.
- `PostAudits` registra alteracoes em `Post` e `Publication`.

## Migrations

- EF Core Migrations.
- Aplicadas automaticamente no startup do Worker.

## Seed data

- 10 Categories (Science, Religion, Space, Animals, Nature, History, Technology, Human Body, Geography, Culture).
- 5 Subcategories por Category (50 Subcategories).
- Configuracoes iniciais em `SystemSettings`:
  - `MAX_CAPTION_CONTENT_LENGTH = 800`.
  - `SIMILARITY_THRESHOLD = 0.80`.
  - `MAX_GENERATION_ATTEMPTS = 3`.

---

# API Contracts

A primeira versao nao expoe API propria. Apenas clientes externos.

## OpenRouter - Texto

### Request

```json
POST https://openrouter.ai/api/v1/chat/completions
Authorization: Bearer <OPENROUTER_API_KEY>
Content-Type: application/json

{
  "model": "google/gemma-4-26b-a4b-it:free",
  "messages": [
    {
      "role": "system",
      "content": "You generate one factual curiosity..."
    },
    {
      "role": "user",
      "content": "Generate a curiosity about <Category>/<Subcategory>."
    }
  ],
  "response_format": { "type": "json_schema", "json_schema": { ... } }
}
```

### Response

```json
{
  "choices": [
    {
      "message": {
        "content": "{\"textContent\":\"...\",\"summary\":\"...\",\"theme\":\"...\",\"sourceUrl\":\"...\",\"category\":\"...\",\"subcategory\":\"...\"}"
      }
    }
  ],
  "usage": {
    "prompt_tokens": 123,
    "completion_tokens": 45,
    "total_tokens": 168
  }
}
```

### Status codes

- 200: Sucesso.
- 401: API key invalida.
- 429: Rate limit.
- 5xx: Erro de servidor.

## OpenRouter - Imagem

### Request

```json
POST https://openrouter.ai/api/v1/images
Authorization: Bearer <OPENROUTER_API_KEY>
Content-Type: application/json

{
  "model": "meta/muse-image",
  "prompt": "A poetic surreal illustration about ..."
}
```

### Response

```json
{
  "data": [
    { "b64_json": "<base64 PNG>" }
  ],
  "usage": { "cost": 0.01 }
}
```

## Meta Graph API

### Criar container

```text
POST https://graph.facebook.com/v17.0/{ig-user-id}/media
  ?image_url=<PRESIGNED_URL>
  &caption=<CAPTION>
  &access_token=<META_ACCESS_TOKEN>
```

### Publicar

```text
POST https://graph.facebook.com/v17.0/{ig-user-id}/media_publish
  ?creation_id=<CREATION_ID>
  &access_token=<META_ACCESS_TOKEN>
```

### Renovar token

```text
GET https://graph.instagram.com/refresh_access_token
  ?grant_type=ig_refresh_token
  &access_token=<META_ACCESS_TOKEN>
```

---

# UI / UX Specifications

A primeira versao **nao possui UI**. Operacao via logs do Worker e comandos administrativos locais na VPS (futuro).

---

# Integracoes Externas

## OpenRouter (texto)

- **Provedor**: OpenRouter.
- **Tipo**: REST.
- **Autenticaacao**: API Key.
- **SLA**: nao documentado.
- **Resiliencia**: retry 3x com backoff exponencial (10s, 20s, 40s cap 120s).
- **Comportamento em falha**: registrar `FailureStep = TextGeneration` e aguardar proximo horario.
- **Versionamento**: lista de modelos varia; verificar `/api/v1/models` periodicamente.

## OpenRouter (imagem)

- **Provedor**: OpenRouter.
- **Tipo**: REST.
- **Autenticaacao**: API Key.
- **SLA**: nao documentado.
- **Resiliencia**: retry 3x.
- **Comportamento em falha**: registrar `FailureStep = ImageGeneration`.

## Meta Graph API

- **Provedor**: Meta.
- **Tipo**: REST.
- **Autenticaacao**: Bearer token (long-lived).
- **SLA**: nao documentado para esta API.
- **Resiliencia**: retry 3x para 429/5xx/timeout.
- **Comportamento em falha**: registrar `FailureStep = InstagramApi`.

## MinIO

- **Provedor**: MinIO local.
- **Tipo**: S3 compativel.
- **Autenticaacao**: AccessKey/SecretKey.
- **SLA**: auto-gerido.
- **Resiliencia**: sem retry adicional (Docker restart).
- **Comportamento em falha**: registrar `FailureStep = ImageStorage`.

---

# Error Handling & Logging Strategy

## Categorias de erro

- Validation (BR-002, BR-003, BR-004, BR-005, BR-007).
- BusinessRule (BR-001, BR-008, BR-009, BR-010, BR-011, BR-012, BR-013).
- ExternalFailure (OpenRouter, Meta, MinIO).
- Unexpected (qualquer outra excecao).

## Formato de log (JSON)

```json
{
  "timestamp": "2026-08-26T17:00:00.000Z",
  "level": "Information",
  "message": "Text generation completed",
  "executionId": "8f31a...",
  "step": "TextGeneration",
  "outcome": "Success",
  "durationMs": 4321,
  "modelId": "google/gemma-4-26b-a4b-it:free",
  "costUsd": 0.0001,
  "tokensIn": 123,
  "tokensOut": 45
}
```

## Log levels

- ERROR: falha definitiva de etapa.
- WARN: rejeicao de conteudo, retry.
- INFO: etapas normais do pipeline.
- DEBUG: detalhes extras (somente em dev).

## Campos obrigatorios

- `timestamp`, `level`, `message`, `executionId`, `step`, `outcome`, `durationMs`.

## Sanitizacao

- Nunca registrar tokens, URLs pre-assinadas, chaves, senhas, client secrets.
- Provider Serilog com redator de campos sensiveis.

## Correlation ID

- Nao ha `correlationId` propagado. Cada etapa registra `executionId` proprio.

---

# Frontend State Management & Routing

Nao aplicavel. Sem frontend na primeira versao.

---

# Testing Requirements

## Cobertura esperada

- 70% no dominio.

## Ferramentas

- `xUnit`.
- `FluentAssertions`.
- `NSubstitute`.

## Organizacao

- `tests/OddOddities.UnitTests/`.

## Mocking

- `NSubstitute` para portas hexagonais.

## CI

- `dotnet test` em todo push na `main`.

---

# Division of Tasks and Product Backlog

## Epicos

- EP-01: Bootstrap Worker.
- EP-02: Pipeline de geracao.
- EP-03: Pipeline de publicacao.
- EP-04: Banco de dados.
- EP-05: Infraestrutura.
- EP-06: Operacao e observabilidade.
- EP-07: Documentacao.

## Features por epico

### EP-01: Bootstrap Worker

- F01: Criar projeto .NET Worker.
- F02: Configurar `appsettings.json` e `AppConfiguration`.
- F03: Configurar Dockerfile multi-stage.
- F04: Configurar `docker-compose.yml`.

### EP-02: Pipeline de geracao

- F05: Adapter OpenRouter texto.
- F06: Adapter OpenRouter imagem.
- F07: Validacao de SourceUrl.
- F08: Similaridade textual.
- F09: Balanceamento de Category/Subcategory.

### EP-03: Pipeline de publicacao

- F10: Adapter Meta Graph API.
- F11: Adapter MinIO.
- F12: ImageSharp processing.
- F13: Renovacao automatica do token.

### EP-04: Banco de dados

- F14: EF Core DbContext.
- F15: Migrations.
- F16: Seeds.
- F17: Repositorios.

### EP-05: Infraestrutura

- F18: Nginx reverse proxy.
- F19: Certbot.
- F20: Tutorial `nginx.md`.

### EP-06: Operacao e observabilidade

- F21: Logs Serilog.
- F22: Middleware global de erros.
- F23: Metricas em logs.
- F24: Renovacao automatica de certificado (job).

### EP-07: Documentacao

- F25: Tutorial `instagram-api.md`.
- F26: Tutorial `openrouter.md`.
- F27: README final.

## Ordem sugerida de implementacao

1. EP-01: bootstrap Worker rodando no Docker.
2. EP-04: banco + migrations + seeds.
3. EP-02: geracao end-to-end com mock da Meta.
4. EP-03: integracao Meta + MinIO + ImageSharp.
5. EP-05: Nginx + Certbot na VPS.
6. EP-06: observabilidade.
7. EP-07: documentacao.

## Definition of Done global

- Codigo revisado pelo proprio autor antes do merge.
- Build limpo.
- Testes passando.
- Logs sem segredos.
- Documentacao atualizada se houve decisao alterada.
