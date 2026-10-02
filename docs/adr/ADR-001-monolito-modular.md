# Architecture Decision Record - Monolito Modular em Docker Compose

## Status

Aceito

## Contexto

O projeto atende a um unico perfil de Instagram, com execucao agendada de aproximadamente 12 posts por mes, em uma VPS Contabo Cloud VPS 4 com 8 GB de RAM e 100 GB SSD. A equipe e composta por uma unica pessoa, com experiencia avancada em .NET, Docker e bancos relacionais.

A carga e baixa, nao ha multi-regiao, nao ha multi-tenant e nao ha requisitos formais de SLA. Solucoes com Kubernetes, Docker Swarm, filas distribuidas ou microservicos representam complexidade operacional que nao agrega valor proporcional ao porte do projeto.

## Decisao

Adotar **monolito modular** empacotado em um unico container Docker do Worker, executado via `docker-compose.yml` na mesma VPS, junto com PostgreSQL, MinIO, Nginx e Certbot.

## Consequencias

**Positivas**

- Menor complexidade operacional.
- Build e deploy em poucos segundos.
- Configuracao centralizada em `docker-compose.yml` versionado.
- Logs estruturados consolidados em um unico container.
- Custo operacional reduzido.
- Facil de auditar e reproduzir.

**Negativas**

- Sem isolamento de falhas entre modulos.
- Sem escalabilidade horizontal granular.
- Publicacao exige reinicializacao completa do container.
- Limite de tamanho da imagem Docker conforme crescimento do codigo.

## Alternativas consideradas

- **Microsservicos**: rejeitada pela complexidade operacional e equipe solo.
- **Serverless**: rejeitada pela dependencia de provedor externo e pela presenca de VPS dedicada.
- **Docker Swarm**: rejeitada pela complexidade adicional sem ganho real.
