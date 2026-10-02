# Architecture Decision Record - Arquitetura Hexagonal (Ports and Adapters)

## Status

Aceito

## Contexto

O pipeline depende de varios adapters externos:

- OpenRouter para geracao de texto e imagem.
- Meta Instagram Graph API para publicacao.
- MinIO para armazenamento.
- PostgreSQL para persistencia.

A escolha de provedores pode mudar (OpenRouter, AWS S3, Meta Graph API v1). Um modelo arquitetural rigido acoplaria o dominio as APIs externas.

## Decisao

Adotar **Hexagonal / Ports and Adapters**, com o dominio no centro e portas dedicadas para cada integracao externa.

**Portas principais:**

- `ITextGenerationPort`
- `IImageGenerationPort`
- `IInstagramPublishingPort`
- `IObjectStoragePort`
- `IPostRepository`
- `IClock`

## Consequencias

**Positivas**

- Troca de provedor sem alterar regras de negocio.
- Testes unitarios isolados por porta.
- Documentacao explicita dos contratos externos.
- Adapters podem ser mockados em testes.

**Negativas**

- Mais arquivos e interfaces.
- Curva de leitura inicial maior.
- Exige disciplina para manter adaptadores fora do dominio.

## Alternativas consideradas

- **Layered / N-Tier**: rejeitada por espalhar uma mesma feature por multiplas pastas.
- **Clean Architecture**: semelhante a hexagonal, mas com nomenclatura menos explicita para o caso.
- **Vertical Slice**: rejeitada por dificultar reuso entre pipelines que compartilham o mesmo adapter.
