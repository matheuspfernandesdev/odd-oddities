# Odd Oddities

Automacao de postagens para o perfil de Instagram **Odd Oddities**.

O Worker .NET em Docker gera, em tres publicacoes semanais, uma curiosidade factual em ingles e uma ilustracao artistica gerada por IA, e publica no Instagram via Meta Graph API. As imagens sao armazenadas em um MinIO local protegido por Nginx com TLS.

## Stack

- .NET 8 Worker em Docker
- PostgreSQL 16 (relacional)
- MinIO (object storage compativel com S3)
- Nginx + Let's Encrypt (reverse proxy HTTPS)
- OpenRouter (geracao de texto e imagem)
- Meta Instagram Graph API (publicacao)
- GitHub Actions + GHCR (CI/CD)
- Docker Compose na VPS

## Estrutura do repositorio

```text
docs/
  architecture.md          # Decisoes arquiteturais e ADRs consolidados
  prd.md                   # Product Requirements Document
  to-be-determined.md      # Decisoes pendentes
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
- [Pendencias](docs/to-be-determined.md)
- [ADRs](docs/adr/)
- [Tutorial Nginx](docs/nginx.md)
- [Tutorial Instagram API](docs/instagram-api.md)
- [Tutorial OpenRouter](docs/openrouter.md)
