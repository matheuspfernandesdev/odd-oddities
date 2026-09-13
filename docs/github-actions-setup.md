# GitHub Actions - Configuracao de Secrets e Variables

Este documento explica como configurar as **Secrets** e **Variables** no GitHub para o deploy automatizado do Odd Oddities.

---

## Arquitetura do Deploy

```
GitHub Actions (push na main)
    -> SSH na VPS (mesma do aws-lambda-api-schedule)
        -> git pull
        -> export env vars (GitHub Secrets + Variables)
        -> docker compose up -d --build
            -> Container Worker (.NET 8)
            -> Container PostgreSQL
            -> Container MinIO
            -> Container Nginx
            -> Container Certbot
```

---

## Onde Configurar

1. Acesse o repositorio no GitHub: https://github.com/matheuspfernandesdev/odd-oddities
2. Vá em **Settings** (configurações do repositorio)
3. No menu lateral, clique em **Secrets and variables > Actions**
4. Você verá duas abas:
   - **Secrets** - valores sensíveis (não aparecem nos logs)
   - **Variables** - valores não sensíveis (aparecem nos logs)

---

## Secrets (valores sensíveis)

Clique em **New repository secret** para cada item:

| Secret | Descrição | Exemplo |
|--------|-----------|---------|
| `SSH_PRIVATE_KEY` | Chave privada SSH da VPS | Conteúdo do arquivo `~/.ssh/id_ed25519` |
| `POSTGRES_PASSWORD` | Senha do PostgreSQL | `sua_senha_forte_aqui` |
| `OPENROUTER_API_KEY` | Chave da API OpenRouter | `sk-or-v1-...` |
| `META_APP_ID` | ID do aplicativo Meta | `4620156194935086` |
| `META_APP_SECRET` | Chave secreta do app Meta | `abc123def456...` |
| `META_ACCESS_TOKEN` | Token de acesso do Instagram (longa duração) | `IGQV...` |
| `INSTAGRAM_USER_ID` | ID numérico da conta Instagram | `17841401234567890` |
| `MINIO_ACCESS_KEY` | Chave de acesso do MinIO | `minioadmin` ou chave custom |
| `MINIO_SECRET_KEY` | Chave secreta do MinIO | `minioadmin` ou chave custom |
| `MINIO_BUCKET_NAME` | Nome do bucket do MinIO | `odd-oddities` |
| `STORAGE_DOMAIN` | Domínio público do storage | `storage.seudominio.com` |
| `TOKEN_ENCRYPTION_KEY` | Chave AES-256-GCM (32 caracteres) | `12345678901234567890123456789012` |

### Como obter cada Secret

#### SSH_PRIVATE_KEY
Mesma chave usada no projeto `aws-lambda-api-schedule`. Se já configurou lá, pode reutilizar.

```bash
# Na VPS, gerar nova chave ou usar existente
ssh-keygen -t ed25519 -C "github-actions-odd-oddities"
cat ~/.ssh/id_ed25519  # copiar conteúdo completo
```

#### POSTGRES_PASSWORD
Gere uma senha forte:
```bash
openssl rand -base64 32
```

#### OPENROUTER_API_KEY
1. Acesse https://openrouter.ai/keys
2. Crie uma nova chave de API
3. Copie o valor

#### META_APP_ID, META_APP_SECRET, META_ACCESS_TOKEN, INSTAGRAM_USER_ID
Veja o tutorial completo em [docs/instagram-api.md](./instagram-api.md)

#### MINIO_ACCESS_KEY, MINIO_SECRET_KEY
Para ambiente de desenvolvimento, pode usar `minioadmin` para ambos.
Para produção, gere valores aleatórios:
```bash
openssl rand -base64 16  # para access key
openssl rand -base64 32  # para secret key
```

#### MINIO_BUCKET_NAME
Nome do bucket onde as imagens serão armazenadas. Ex: `odd-oddities`

#### STORAGE_DOMAIN
Domínio público configurado no Nginx para acessar o MinIO. Ex: `storage.odd-oddities.com`

#### TOKEN_ENCRYPTION_KEY
Chave de 32 caracteres para criptografia AES-256-GCM dos tokens:
```bash
openssl rand -base64 32 | cut -c1-32
```

---

## Variables (valores não sensíveis)

Clique em **New repository variable** para cada item:

| Variable | Descrição | Valor Padrão |
|----------|-----------|--------------|
| `VPS_HOST` | IP ou hostname da VPS | `147.93.181.144` (mesma do outro projeto) |
| `VPS_USER` | Usuário SSH da VPS | `root` |
| `VPS_DEPLOY_PATH` | Caminho de deploy na VPS | `/var/www/odd-oddities` |
| `TEXT_MODEL_ID` | Modelo de texto do OpenRouter | `google/gemma-4-26b-a4b-it:free` |
| `IMAGE_MODEL_ID` | Modelo de imagem do OpenRouter | `meta/muse-image` |
| `SCHEDULE_HOUR_UTC` | Hora UTC do agendamento | `17` |
| `SCHEDULE_TIMEZONE` | Fuso horário | `Eastern Standard Time` |
| `SCHEDULE_DAYS` | Dias da semana | `TUE,THU,SAT` |

### Explicação das Variables

#### VPS_HOST, VPS_USER, VPS_DEPLOY_PATH
Mesmas configurações do projeto `aws-lambda-api-schedule`. O deploy será feito na mesma VPS, mas em diretório diferente.

#### TEXT_MODEL_ID, IMAGE_MODEL_ID
Modelos do OpenRouter para gerar texto e imagem. Veja modelos disponíveis em https://openrouter.ai/models

#### SCHEDULE_HOUR_UTC
Hora UTC em que o post será publicado. O Worker converte para o fuso horário configurado.

#### SCHEDULE_TIMEZONE
Fuso horário usado para calcular o horário do post. Exemplos:
- `Eastern Standard Time` (US Eastern)
- `America/Sao_Paulo` (Brasil)
- `UTC`

#### SCHEDULE_DAYS
Dias da semana em que o post será publicado. Valores válidos:
- `MON`, `TUE`, `WED`, `THU`, `FRI`, `SAT`, `SUN`

Exemplo: `TUE,THU,SAT` = Terça, Quinta e Sábado

---

## Setup Inicial na VPS

Se a VPS já tem o projeto `aws-lambda-api-schedule` configurado, você só precisa criar o diretório de deploy:

```bash
# Na VPS
sudo mkdir -p /var/www/odd-oddities
sudo chown $USER:$USER /var/www/odd-oddities
```

Se for uma VPS nova, siga o setup completo:

```bash
# 1. Instalar Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
sudo usermod -aG docker $USER
exit  # relogar

# 2. Instalar Git
sudo apt update && sudo apt install -y git

# 3. Criar diretório de deploy
sudo mkdir -p /var/www
sudo chown $USER:$USER /var/www

# 4. Configurar chave SSH para GitHub Actions
ssh-keygen -t ed25519 -C "github-actions-odd-oddities"
cat ~/.ssh/id_ed25519.pub >> ~/.ssh/authorized_keys
cat ~/.ssh/id_ed25519  # copiar esta chave PRIVADA para GitHub Secret SSH_PRIVATE_KEY
```

---

## Deploy

### Automático (push na main)
1. Configure todas as secrets e variables no GitHub
2. Dê um push na branch `main`
3. O workflow roda automaticamente e faz deploy na VPS

### Manual
1. Vá em **Actions > Deploy to VPS**
2. Clique em **Run workflow**
3. Selecione a branch e clique em **Run workflow**

---

## Comandos Úteis na VPS

```bash
# Ver logs do Worker
docker logs -f odd-oddities-worker

# Ver logs de todos os containers
docker compose logs -f

# Reiniciar todos os containers
docker compose restart

# Parar todos os containers
docker compose down

# Status dos containers
docker ps

# Reconstruir e reiniciar
docker compose up -d --build

# Acessar shell do container Worker
docker exec -it odd-oddities-worker /bin/sh
```

---

## Troubleshooting

### Workflow falha com erro SSH
- Verificar se `SSH_PRIVATE_KEY` está correta
- Verificar se a chave pública está no `~/.ssh/authorized_keys` da VPS
- Verificar se `VPS_HOST` e `VPS_USER` estão corretos

### Container não inicia
```bash
docker logs odd-oddities-worker
docker compose logs
```

### Erro de conexão com PostgreSQL
- Verificar se `POSTGRES_PASSWORD` está correta
- Verificar se o container `odd-oddities-postgres` está rodando

### Erro de conexão com Instagram API
- Verificar se `META_ACCESS_TOKEN` não expirou
- Ver tutorial em [docs/instagram-api.md](./instagram-api.md)

### Erro de MinIO
- Verificar se `MINIO_ACCESS_KEY` e `MINIO_SECRET_KEY` estão corretas
- Verificar se o bucket foi criado (o Worker cria automaticamente no startup)

---

## Checklist de Configuração

- [ ] SSH_PRIVATE_KEY configurada
- [ ] POSTGRES_PASSWORD configurada
- [ ] OPENROUTER_API_KEY configurada
- [ ] META_APP_ID configurada
- [ ] META_APP_SECRET configurada
- [ ] META_ACCESS_TOKEN configurada
- [ ] INSTAGRAM_USER_ID configurada
- [ ] MINIO_ACCESS_KEY configurada
- [ ] MINIO_SECRET_KEY configurada
- [ ] MINIO_BUCKET_NAME configurada
- [ ] STORAGE_DOMAIN configurada
- [ ] TOKEN_ENCRYPTION_KEY configurada
- [ ] VPS_HOST configurada
- [ ] VPS_USER configurada
- [ ] VPS_DEPLOY_PATH configurada
- [ ] TEXT_MODEL_ID configurada
- [ ] IMAGE_MODEL_ID configurada
- [ ] SCHEDULE_HOUR_UTC configurada
- [ ] SCHEDULE_TIMEZONE configurada
- [ ] SCHEDULE_DAYS configurada
- [ ] Diretório `/var/www/odd-oddities` criado na VPS
