# Architecture Decision Record - PostgreSQL como Banco de Dados

## Status

Aceito

## Contexto

O dominio possui entidades com relacionamentos claros (Post, Category, Subcategory, GenerationAttempt, Publication, SystemSetting). O volume e baixo (aproximadamente 12 posts por mes) e o historico sera mantido permanentemente. O projeto ja adota Docker, o que simplifica a operacao de um banco relacional em container.

## Decisao

Adotar **PostgreSQL 16** em container Docker, gerenciado via EF Core Migrations.

## Consequencias

**Positivas**

- Suporte completo a ACID e integridade referencial.
- Migrations versionadas com EF Core.
- Conexoes via Npgsql com pool nativo.
- Banco ja preparado para evolucao de consultas analiticas.
- Familiaridade da equipe com a tecnologia.

**Negativas**

- Requer container adicional e volume persistente.
- Necessita backups explicitos para evitar perda (nao obrigatorio na POC).
- Tuning de parametros pode ser necessario em producao.

## Alternativas consideradas

- **SQLite**: rejeitada pela limitacao de concorrencia escrita e impossibilidade de multiplos containers acessando o mesmo banco sem complicacoes.
- **MySQL**: equivalente funcional, mas PostgreSQL oferece melhor suporte a JSON, indices compostos e operadores textuais para a similaridade.
- **NoSQL**: rejeitada pelos relacionamentos explicitos do dominio.
