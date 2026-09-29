# Odd Oddities

Este e o projeto que automatiza o perfil do Instagram [**@oddities.odd**](https://www.instagram.com/oddities.odd/) utilizando inteligencia artificial, algoritmos de similaridade e publicacao via API oficial da Meta.

O Worker .NET em Docker roda um pipeline que, tres vezes por semana, gera automaticamente uma curiosidade factual bizarra (em ingles), uma ilustracao artistica criada por IA no estilo surrealista/poetico da pagina, e publica o post no feed do Instagram sem intervencao humana.

## Como funciona

O pipeline (`TextGeneration` → `ImageGeneration` → `Publication`) executado pelo `PipelineOrchestrator`:

1. **Geracao de texto** — um LLM (via OpenRouter) escreve uma curiosidade factual com titulo, resumo e fonte.
2. **Geracao de imagem** — um modelo de imagem (via OpenRouter) cria a arte correspondente, processada com ImageSharp (formato/quadrado, marca d'agua).
3. **Publicacao** — a imagem vai para o MinIO e o post e publicado no Instagram via Meta Graph API.

Os modelos preferidos e a cadeia de fallback (com limites de custo) sao configuraveis dinamicamente — se o modelo principal falha ou estoura o orcamento, o proximo da lista assume.

## Regras do pipeline

- **Conteudo em ingles**, no escopo "odd oddities" (curiosidades estranhas e reais).
- **Anti-repeticao**: `ContentHash` (SHA-256) rejeita textos duplicados exatos.
- **Similaridade textual**: algoritmo de Jaccard sobre tokens normalizados compara o resumo com os posts dos ultimos 90 dias; similaridade >= 80% rejeita o conteudo (BR-005).
- **Retentativas**: conteudo rejeitado re-tenta a geracao ate 3 vezes; erros transitorios de API usam retry com backoff (validacao/rejeicao de negocio nao retenta).
- **Auditoria**: cada tentativa de geracao e registrada no PostgreSQL (`GenerationAttempt`).

## Stack

- .NET 8 Worker (arquitetura hexagonal) em Docker
- PostgreSQL 16 (persistencia e consultas de similaridade)
- MinIO (object storage compativel com S3) + Nginx + Let's Encrypt
- OpenRouter (geracao de texto e imagem)
- ImageSharp (processamento visual)
- Meta Instagram Graph API (publicacao)
- GitHub Actions + GHCR (CI/CD), Docker Compose na VPS

## Estrutura do repositorio

```text
docs/
  architecture.md          # Decisoes arquiteturais e ADRs consolidados
  prd.md                   # Product Requirements Document
  adr/                     # Architecture Decision Records
  nginx.md                 # Tutorial Nginx + Let's Encrypt + Certbot (Ubuntu LTS)
  instagram-api.md         # Tutorial Instagram Graph API do zero
  openrouter.md            # Tutorial OpenRouter
  the-idea.md              # Ideia original

assets/
  logo-watermark.png       # Identidade visual gerada para o projeto
```

## Como rodar localmente

A documentacao completa esta em `docs/`. O fluxo geral:

1. Provisionar VPS Ubuntu LTS com Docker e Docker Compose.
2. Configurar dominio, Nginx, Let's Encrypt e Certbot seguindo `docs/nginx.md`.
3. Criar conta no OpenRouter e gerar chave seguindo `docs/openrouter.md`.
4. Configurar app Meta e token seguindo `docs/instagram-api.md`.
5. Configurar GitHub Actions Secrets e Variables.
6. Realizar deploy via `docker compose pull && docker compose up -d`.

## Documentacao

- [Visao geral e arquitetura](docs/architecture.md)
- [PRD](docs/prd.md)
- [ADRs](docs/adr/)
- [Tutorial Nginx](docs/nginx.md)
- [Tutorial Instagram API](docs/instagram-api.md)
- [Tutorial OpenRouter](docs/openrouter.md)

---

Tudo isso existe por um motivo: manter o [@oddities.odd](https://www.instagram.com/oddities.odd/) postando curiosidades incomuns com arte gerada por IA, sozinho, semana apos semana. Segue la! 🧿
