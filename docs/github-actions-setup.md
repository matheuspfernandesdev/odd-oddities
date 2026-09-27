# GitHub Actions - Configuracao de Secrets e Variables

Guia completo, passo a passo, para publicar o Odd Oddities na VPS. Execute as secoes na ordem.

---

## Arquitetura do Deploy

```
GitHub Actions (workflow_dispatch - gatilho MANUAL)
    -> SSH na VPS (chave existente, mesma do aws-lambda-api-schedule)
        -> ssh-keyscan github.com (known_hosts)
        -> mkdir -p do diretorio de deploy (cria se nao existir)
        -> git clone / git pull
        -> grava /var/www/odd-oddities/.env (chmod 600)
        -> docker compose up -d --build
            -> Container Worker (.NET 8)
            -> Container PostgreSQL
```

Sobem **apenas 2 containers** deste projeto: `odd-oddities-worker` e `odd-oddities-postgres`.

### Dependencia externa: vps-infra

MinIO, Nginx e Certbot **nao fazem parte deste deploy**. Eles rodam no repositorio
[`vps-infra`](C:\binaryten-git\vps-infra), no diretorio `/opt/vps-infra` da mesma VPS.

| Componente | Container | Como o Odd Oddities usa |
|---|---|---|
| MinIO | `vps-minio` | Via endpoint publico `https://s3.binaryten.com.br` (rede do app nao se conecta na rede do MinIO) |
| Nginx | `vps-nginx` | Serve o S3 no dominio `s3.binaryten.com.br` (80/443) |
| Certbot | `vps-certbot` | Renova o certificado do S3 (cron, sem interacao) |

- Buckets ja criados pelo `vps-infra`: `odd-oddities-dev` e `odd-oddities-prod` (usamos **prod**)
- O Worker **nao tem dominio proprio** (e um worker de segundo plano) - nao precisa de DNS, vhost nem certificado. Nada de `add-new-service.md` se aplica aqui.
- Referencias: `vps-infra/docs/bucket-management.md` e `vps-infra/docs/setup-guide.md`

### Pre-requisito critico

Todo este fluxo **depende do `vps-infra` estar no ar**. Se o MinIO estiver parado, o Worker falha ao publicar imagens.

---

## Passo 1 - Pre-requisitos na VPS

Execute os comandos abaixo **um por vez**, na ordem. Cole cada bloco inteiro no terminal da VPS.

### 1.1 Confirmar que o `vps-infra` esta de pé

```bash
cd /opt/vps-infra && docker compose ps
```

Esperado: o container `vps-minio` com status **`healthy`**. Se nao estiver, suba o `vps-infra` antes de continuar (o Odd Oddities depende dele).

### 1.2 Diretorio de deploy

Nao e preciso criar nada na mao: o workflow roda `mkdir -p $VPS_DEPLOY_PATH` antes do
clone e **cria o diretorio automaticamente** se nao existir.

Se quiser conferir (opcional):

```bash
ls -ld /var/www/odd-oddities
```

### 1.3 Conferir a chave SSH existente (NAO gere chave nova)

> **Importante:** nao rode `ssh-keygen`. O Passo 1 so **le** a chave que ja existe na VPS (a mesma do projeto `aws-lambda-api-schedule`).
>
> Se voce iniciou um `ssh-keygen` por engano e ele esta pedindo `Enter file in which to save the key`, **nao digite nada la** - pressione `Ctrl+C` para abortar. Comandos como `cat` sao executados **depois**, em um prompt novo, nunca dentro do `ssh-keygen`.

```bash
ls -la ~/.ssh/
```

Esperado: existir `id_ed25519` (chave privada) e `id_ed25519.pub` (chave publica). Se existirem, va direto para o 1.4.

**Se a chave nao existir:** recupere a chave do projeto `aws-lambda-api-schedule` (GitHub -> repo `aws-lambda-api-schedule` -> Settings -> Secrets -> `SSH_PRIVATE_KEY`), ou gere uma unica vez com o comando abaixo e responda os prompts apenas com `Enter`:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519
```

> `ssh-keygen` e interativo: ele pergunta o **caminho do arquivo** e a **passphrase**. Aceite o caminho default com `Enter` e deixe a passphrase vazia (dois `Enter`). Nao digite comandos nesses prompts - eles so aceitam caminhos/senhas.

Depois, se a chave for nova, adicione a **publica** como deploy key (item 1.4).

### 1.4 Conferir a chave publica (deploy key no GitHub)

```bash
cat ~/.ssh/id_ed25519.pub
```

> O `cat` e um comando **normal de shell** - rode em um prompt novo, apos o `ssh-keygen` ja ter terminado (ou abortado com `Ctrl+C`). Copie a saida inteira (comeca com `ssh-ed25519 ...`).

Cole no GitHub: repo **odd-oddities** -> **Settings > Deploy keys > Add deploy key**, com o checkbox **"Allow write access" DESMARCADO** (read-only).

### 1.5 Testar o acesso da VPS ao GitHub

```bash
ssh -T git@github.com
```

- **Sucesso** (algo como `Hi matheuspfernandesdev/odd-oddities! You've successfully authenticated...` ou `Hi matheuspfernandesdev!...`) -> passe para o Passo 2.
- **Falhou** (`Permission denied (publickey)`) -> garanta que a chave publica do 1.4 esta salva como deploy key e rode o comando de novo.

### 1.6 Copiar a chave privada para o Secret `SSH_PRIVATE_KEY`

Este e o conteudo que voce vai colar no GitHub no Passo 3 (Secret `SSH_PRIVATE_KEY`).

```bash
cat ~/.ssh/id_ed25519
```

Copie a saida **inteira**, incluindo as linhas:

```
-----BEGIN OPENSSH PRIVATE KEY-----
...
-----END OPENSSH PRIVATE KEY-----
```

> **Onde colar:** GitHub -> repo **odd-oddities** -> Settings > Secrets and variables > Actions > Secrets > New repository secret, nome `SSH_PRIVATE_KEY`, valor = a saida do `cat` acima.
>
> **Nao misture comandos:** `cat ~/.ssh/id_ed25519` e um comando separado. Se voce o digitar dentro de um prompt do `ssh-keygen`, o ssh-keygen tenta salvar a chave num arquivo com esse nome e falha com `Saving key "cat ~/.ssh/id_ed25519" failed: No such file or directory`. Nesse caso, `Ctrl+C` e rode o `cat` em um prompt novo.

O `known_hosts` do `github.com` e cuidado pelo proprio workflow (roda `ssh-keyscan` antes do clone).

---

## Passo 2 - Criar a access key do MinIO

O Worker usa uma **access key de servico** (nao o root, nao `minioadmin`) com restricao ao bucket `odd-oddities-prod`.

1. Abra o Console: https://minio-console.binaryten.com.br
2. Login com as credenciais root do `.env` do `vps-infra` (basic auth do Nginx + usuario/senha `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`)
3. **Access Keys > Create access key**
4. Anexe a policy **`odd-oddities-prod`**
5. Copie o **Access Key** e o **Secret Key** (so aparecem uma vez)
6. Guarde para o Passo 3 (secrets `MINIO_ACCESS_KEY` e `MINIO_SECRET_KEY`)

> O bucket `odd-oddities-prod` ja foi criado pelo `vps-infra` (`minio/init-buckets.sh`).
> O Worker **nao cria bucket**: a policy dele nao tem `s3:CreateBucket` - se o bucket faltar, o pipeline falha com erro de acesso.
> Detalhes: `vps-infra/docs/bucket-management.md`

---

## Passo 3 - Secrets no GitHub (valores sensiveis)

1. Repo no GitHub: https://github.com/matheuspfernandesdev/odd-oddities
2. **Settings > Secrets and variables > Actions > Secrets > New repository secret**

| Secret | Valor | Exemplo / onde obter |
|--------|-------|----------------------|
| `SSH_PRIVATE_KEY` | Chave privada ja existente na VPS | Saida de `cat ~/.ssh/id_ed25519` (Passo 1.6, mesma do aws-lambda-api-schedule) |
| `POSTGRES_PASSWORD` | Senha forte para o Postgres | `openssl rand -base64 32` |
| `OPENROUTER_API_KEY` | Chave da API OpenRouter | https://openrouter.ai/keys |
| `META_APP_ID` | ID do aplicativo Meta | ver [instagram-api.md](./instagram-api.md) |
| `META_APP_SECRET` | Chave secreta do app Meta | ver [instagram-api.md](./instagram-api.md) |
| `META_ACCESS_TOKEN` | Token Instagram (longa duracao) | ver [instagram-api.md](./instagram-api.md) |
| `INSTAGRAM_USER_ID` | ID numerico da conta Instagram | ver [instagram-api.md](./instagram-api.md) |
| `MINIO_ACCESS_KEY` | Access key de servico do MinIO | Passo 2 (Console) |
| `MINIO_SECRET_KEY` | Secret key de servico do MinIO | Passo 2 (Console) |
| `MINIO_BUCKET_NAME` | `odd-oddities-prod` | fixo |
| `STORAGE_DOMAIN` | `s3.binaryten.com.br` | fixo (endpoint publico do vps-infra) |
| `TOKEN_ENCRYPTION_KEY` | Chave AES-256-GCM (32 caracteres) | `openssl rand -base64 32 \| cut -c1-32` |

### Observacoes

- **`MINIO_ACCESS_KEY` / `MINIO_SECRET_KEY`**: nao use `minioadmin` nem root - e a access key de servico do Passo 2, restrita a policy `odd-oddities-prod`.
- **`STORAGE_DOMAIN`**: e o dominio puro (sem `https://`) - o `docker-compose.yml` monta `https://${STORAGE_DOMAIN}`.
- **`TOKEN_ENCRYPTION_KEY`**: exatamente 32 caracteres (AES-256). Se gerar outra, tokens ja criptografados no banco deixam de ser legiveis - nao troque depois do primeiro deploy.

---

## Passo 4 - Variables no GitHub (valores nao sensiveis)

**Settings > Secrets and variables > Actions > Variables > New repository variable**

| Variable | Valor | Descricao |
|----------|-------|-----------|
| `VPS_HOST` | `147.93.181.144` | IP da VPS (mesma do aws-lambda-api-schedule) |
| `VPS_USER` | `root` | Usuario SSH da VPS |
| `VPS_DEPLOY_PATH` | `/var/www/odd-oddities` | Diretorio de deploy |
| `TEXT_MODEL_ID` | `google/gemma-4-26b-a4b-it:free` | Modelo de texto preferido (1o da cadeia de fallback) |
| `IMAGE_MODEL_ID` | `openai/gpt-image-2` | Modelo de imagem preferido (1o da cadeia de fallback) |
| `SCHEDULE_HOUR_UTC` | `17` | Hora UTC do post |
| `SCHEDULE_TIMEZONE` | `Eastern Standard Time` | Fus horario do calculo |
| `SCHEDULE_DAYS` | `TUE,THU,SAT` | Dias da semana (MON..SUN, separados por virgula) |

### Explicacao

- **`TEXT_MODEL_ID` / `IMAGE_MODEL_ID`**: modelos **preferidos** do OpenRouter. Se falharem, o Worker busca o catalogo dinamico (`GET /models`) e tenta os proximos candidatos free/baratos dentro dos tetos de `ModelSelection` (ver `docs/adr/ADR-008-modelo-dinamico-fallback-custo.md`). Catalogo: https://openrouter.ai/models
- **`SCHEDULE_TIMEZONE`**: exemplos validos - `Eastern Standard Time`, `America/Sao_Paulo`, `UTC`
- **`SCHEDULE_DAYS`**: ex. `TUE,THU,SAT` = terca, quinta e sabado

---

## Passo 5 - Executar o deploy

O deploy e **manual** (o `push` para `main` nao dispara nada - esta comentado de proposito no workflow).

1. GitHub -> **Actions > Deploy to VPS**
2. Clique em **Run workflow**
3. Selecione a branch `main` e clique em **Run workflow**

### O que acontece por dentro

1. Conecta via SSH na VPS usando `SSH_PRIVATE_KEY`
2. Garante `github.com` no `known_hosts` (`ssh-keyscan`)
3. `mkdir -p` do diretorio de deploy (cria se nao existir) e `git clone` (primeira vez) ou `git pull` em `/var/www/odd-oddities`
4. Grava o arquivo `/var/www/odd-oddities/.env` com todas as Secrets + Variables (`chmod 600`)
5. `docker compose up -d --build` (o Docker Compose le o `.env` automaticamente)
6. Mostra `docker compose ps` e as ultimas linhas de log do Worker

### Reativar deploy automatico (opcional, futuro)

Se um dia quiser voltar a deployar a cada push na `main`, descomente as linhas `push:` em `.github/workflows/deploy.yml`. Enquanto isso nao acontecer, o gatilho e somente manual.

---

## O arquivo `.env` na VPS

- Caminho: `/var/www/odd-oddities/.env` (permissao `600`, dono root)
- E **gravado pelo workflow a cada deploy** - nao e versionado e nao deve ser copiado para o repositorio
- Como o `docker compose` le `.env` do proprio diretorio, qualquer comando manual roda com as credenciais corretas:

```bash
cd /var/www/odd-oddities
docker compose up -d --build   # seguro: usa o .env ja gravado
```

- Se precisar trocar um valor na mao: edite o `.env`, rode `docker compose up -d` e **depois** atualize o valor tambem no GitHub (senao o proximo deploy sobrescreve a mudanca).

---

## Comandos Uteis na VPS

```bash
cd /var/www/odd-oddities

# Logs do Worker
docker logs -f odd-oddities-worker

# Logs de todos os containers do projeto
docker compose logs -f

# Status
docker compose ps

# Reiniciar / parar
docker compose restart
docker compose down

# Reconstruir e reiniciar (usa o .env)
docker compose up -d --build

# Shell do container Worker
docker exec -it odd-oddities-worker /bin/sh
```

---

## Troubleshooting

### Workflow falha com erro SSH
- Verificar se `SSH_PRIVATE_KEY` esta correta (Passo 1.6 - `cat ~/.ssh/id_ed25519`, com as linhas BEGIN/END)
- Verificar se a chave **publica** correspondente esta em `~/.ssh/authorized_keys` da VPS
- Verificar `VPS_HOST` e `VPS_USER`

### Erro `Saving key "cat ~/.ssh/id_ed25519" failed: No such file or directory`
- Voce digitou o comando `cat` **dentro** do prompt interativo do `ssh-keygen` (que esperava um caminho de arquivo)
- Solucao: `Ctrl+C` para abortar o `ssh-keygen` (nenhuma chave foi criada/sobrescrita) e rode `cat ~/.ssh/id_ed25519` como comando **separado** em um prompt novo
- `cat` e sempre um comando de shell normal - nunca dentro de prompts do `ssh-keygen`

### `git clone` falha: `Host key verification failed` ou `Permission denied (publickey)`
- Host key: o workflow roda `ssh-keyscan` antes do clone - se falhou, rode manualmente na VPS: `ssh-keyscan -H github.com >> ~/.ssh/known_hosts`
- Permission: `ssh -T git@github.com` na VPS (Passo 1) - se negar, adicione a pubkey da VPS como **Deploy key read-only** no repo

### Container nao inicia / Worker reinicia em loop
```bash
cd /var/www/odd-oddities
docker logs odd-oddities-worker
docker compose logs
```

### Erro de conexao com PostgreSQL
- Conferir se `/var/www/odd-oddities/.env` existe e tem `POSTGRES_PASSWORD`
- Conferir se `docker compose ps` mostra `odd-oddities-postgres` healthy
- A connection string e montada no nivel raiz (`ConnectionStrings__DefaultConnection`) apontando para `Host=postgres` - nao mexa no aninhamento

### Erro de bucket / `Access Denied` ao salvar imagem
- O Worker **nao cria buckets**. O bucket `odd-oddities-prod` precisa existir no `vps-infra` (`minio/init-buckets.sh`)
- Conferir `MINIO_BUCKET_NAME=odd-oddities-prod`
- Conferir se a access key foi criada com a policy **`odd-oddities-prod`** (Passo 2)
- Conferir se o `vps-infra` esta no ar: `cd /opt/vps-infra && docker compose ps`

### Erro de conexao com Instagram API
- Verificar se `META_ACCESS_TOKEN` nao expirou
- Tutorial: [docs/instagram-api.md](./instagram-api.md)

### Deploy rodou, mas o `.env` ficou incompleto
- O workflow grava o `.env` inteiro a cada execucao - rode o deploy de novo
- Conferir se todas as Secrets/Variables existem (checklist abaixo) - valor ausente entra vazio no `.env`

---

## Checklist de Configuracao

### VPS (uma vez)
- [ ] `vps-infra` no ar (`/opt/vps-infra` - MinIO healthy)
- [ ] `ssh -T git@github.com` funciona (ou deploy key read-only adicionada)

> O diretorio `/var/www/odd-oddities` **nao precisa ser criado manualmente** - o workflow cria com `mkdir -p` a cada deploy.

### MinIO (uma vez)
- [ ] Access key de servico criada no Console com policy `odd-oddities-prod`

### GitHub - Secrets (12)
- [ ] `SSH_PRIVATE_KEY`
- [ ] `POSTGRES_PASSWORD`
- [ ] `OPENROUTER_API_KEY`
- [ ] `META_APP_ID`
- [ ] `META_APP_SECRET`
- [ ] `META_ACCESS_TOKEN`
- [ ] `INSTAGRAM_USER_ID`
- [ ] `MINIO_ACCESS_KEY`
- [ ] `MINIO_SECRET_KEY`
- [ ] `MINIO_BUCKET_NAME` (`odd-oddities-prod`)
- [ ] `STORAGE_DOMAIN` (`s3.binaryten.com.br`)
- [ ] `TOKEN_ENCRYPTION_KEY`

### GitHub - Variables (8)
- [ ] `VPS_HOST`
- [ ] `VPS_USER`
- [ ] `VPS_DEPLOY_PATH`
- [ ] `TEXT_MODEL_ID`
- [ ] `IMAGE_MODEL_ID`
- [ ] `SCHEDULE_HOUR_UTC`
- [ ] `SCHEDULE_TIMEZONE`
- [ ] `SCHEDULE_DAYS`

### Deploy
- [ ] Workflow **Deploy to VPS** executado com sucesso
- [ ] `/var/www/odd-oddities/.env` gravado na VPS
- [ ] `docker compose ps` com worker + postgres healthy
- [ ] Logs do Worker sem erros (`docker logs odd-oddities-worker`)
