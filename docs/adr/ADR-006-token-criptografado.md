# Architecture Decision Record - Token Meta Renovado Criptografado no PostgreSQL

## Status

Aceito

## Contexto

A Instagram Graph API exige um access token. O token de longa duracao expira em aproximadamente 60 dias e precisa ser renovado antes da expiracao. A documentacao oficial indica que a renovacao deve ser feita por chamada ao endpoint `/refresh_access_token`.

Manter o token apenas em variavel de ambiente nao funciona porque o processo de renovacao gera um novo token que precisa ser persistido para sobreviver a reinicializacoes do container.

## Decisao

Persistir o token Meta ativo **criptografado com AES-256-GCM** no PostgreSQL, em uma tabela de configuracoes segura (`SystemSetting`). A chave mestra fica exclusivamente em **GitHub Actions Secrets** e e injetada como variavel de ambiente.

- Token inicial: lido da variavel de ambiente `META_ACCESS_TOKEN`.
- Rotina periodica verifica a data de expiracao.
- Renovacao automatica ocorre quando faltam menos de 14 dias.
- Novo token criptografado substitui o anterior.
- A chave mestra **nao** e armazenada no banco.

## Consequencias

**Positivas**

- Token sobrevive a reinicializacao do container.
- Renovacao automatica reduz risco de expiracao.
- Em caso de revogacao, o sistema registra erro e exige reautorizacao manual.
- Chave mestra nunca persiste no banco.

**Negativas**

- Mais um segredo critico a ser gerenciado.
- Comprometimento da chave mestra exige rotacao manual e recriptografia.
- Adiciona dependencia do Postgres para manter o Worker funcionando.

## Alternativas consideradas

- **Renovacao 100% manual**: rejeitada pelo risco operacional.
- **Token em arquivo na VPS**: rejeitado pela dificuldade de backup e controle de acesso.
- **Vault externo**: rejeitado pela complexidade adicional.
