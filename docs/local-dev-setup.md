# Guia de Desenvolvimento Local — Odd Oddities

Passo a passo para rodar o Worker localmente no Windows (Visual Studio) com PostgreSQL local e MinIO compartilhado na VPS.

> **Regra de configuracao:** valores nao-sensiveis ficam no `appsettings.Development.json` (Git). Segredos ficam nos User Secrets (fora do Git). Veja `architecture.md` → Configuration Layering.

---

## Pre-requisitos

| Item | Detalhe |
|---|---|
| Docker Desktop | Rodando (verificar icone na bandeja) |
| .NET SDK 8.0 | `dotnet --version` |
| Visual Studio | Com workload ASP.NET |
| Acesso a VPS | MinIO bucket `odd-oddities-dev` ja criado |

---

## 1. PostgreSQL local (Docker)

```powershell
docker compose -f docker-compose.dev.yml up -d
```

Status esperado:

```powershell
docker compose -f docker-compose.dev.yml ps
# NAME                           STATUS      PORTS
# odd-oddities-postgres-dev      running     0.0.0.0:5432->5432/tcp
```

Para parar (sem perder dados): `docker compose -f docker-compose.dev.yml down`
Para apagar tudo: `docker compose -f docker-compose.dev.yml down -v`

---

## 2. Credenciais (User Secrets)

User Secrets salva segredos fora do Git (`%APPDATA%\Microsoft\UserSecrets\`). Sobrescreve valores do appsettings.

### 2.1 — MinIO Access Key

Criar no Console MinIO:
1. `https://minio-console.binaryten.com.br` → login root
2. **Access Keys** → **Create access key** → anexar policy `odd-oddities-dev`
3. Copiar Access Key e Secret Key (aparecem uma vez)

```powershell
dotnet user-secrets set "AppConfiguration:MinIO:AccessKey" "<access-key>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:MinIO:SecretKey" "<secret-key>" --project src\OddOddities.Worker
```

### 2.2 — OpenRouter API Key

```powershell
dotnet user-secrets set "AppConfiguration:OpenRouter:ApiKey" "<sua-chave>" --project src\OddOddities.Worker
```

### 2.3 — Meta (Instagram)

```powershell
dotnet user-secrets set "AppConfiguration:Meta:AppId" "<app-id>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:AppSecret" "<app-secret>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:AccessToken" "<access-token>" --project src\OddOddities.Worker
dotnet user-secrets set "AppConfiguration:Meta:InstagramUserId" "<user-id>" --project src\OddOddities.Worker
```

### 2.4 — Token Encryption

```powershell
dotnet user-secrets set "AppConfiguration:TokenEncryption:Key" "<chave-32-char>" --project src\OddOddities.Worker
```

Gerar chave: `openssl rand -base64 32`

### 2.5 — Confirmar tudo

```powershell
dotnet user-secrets list --project src\OddOddities.Worker
```

Esperado:

```
ConnectionStrings:DefaultConnection = Host=localhost;...Password=odd_oddities_dev
AppConfiguration:MinIO:AccessKey = <value>
AppConfiguration:MinIO:SecretKey = <value>
AppConfiguration:OpenRouter:ApiKey = <value>
AppConfiguration:Meta:AppId = <value>
AppConfiguration:Meta:AppSecret = <value>
AppConfiguration:Meta:AccessToken = <value>
AppConfiguration:Meta:InstagramUserId = <value>
AppConfiguration:TokenEncryption:Key = <value>
```

> **Atenção:** a ConnectionString usa `ConnectionStrings:DefaultConnection` (raiz), **não** `AppConfiguration:ConnectionStrings:DefaultConnection`. O `GetConnectionString()` lê da raiz.

---

## 3. appsettings.Development.json (Git)

Este arquivo contem **defaults nao-sensiveis**. Nao coloque segredos aqui.

| Config | Valor | Sensivel? |
|---|---|---|
| MinIO Endpoint | `https://s3.binaryten.com.br` | Nao |
| MinIO BucketName | `odd-oddities-dev` | Nao |
| MinIO PublicEndpoint | `https://s3.binaryten.com.br` | Nao |
| OpenRouter ModelIds | `google/gemma-4-26b-a4b-it:free`, `google/gemini-3.1-flash-lite-image` | Nao |
| ModelSelection | Tetos de fallback/custo (defaults no codigo) | Nao |
| Schedule | HourUtc=17, Timezone, Days | Nao |
| ImageProcessing | 1080x1080, Quality=85 | Nao |
| ConnectionStrings | `Password=CHANGE_ME` (placeholder) | Nao (real fica no User Secrets) |

---

## 4. Rodar no Visual Studio

1. Docker Desktop rodando
2. PostgreSQL rodando (`docker compose -f docker-compose.dev.yml up -d`)
3. User Secrets configurados (passo 2)
4. Abrir `OddOddities.Worker.csproj` no Visual Studio
5. F5

O Worker vai:
- Conectar no PostgreSQL local (aplica migrations automaticamente)
- Conectar no MinIO da VPS via HTTPS
- Iniciar o scheduler

---

## Resumo: onde vive cada variavel

| Variavel | appsettings | User Secrets | docker-compose |
|---|---|---|---|
| MinIO Endpoint | `https://s3...` | — | — |
| MinIO BucketName | `odd-oddities-dev` | — | — |
| MinIO AccessKey | — | `adgu89l...` | — |
| MinIO SecretKey | — | `2qaWBt...` | — |
| PG Host/Port/DB | placeholder | `Host=localhost;...` | `5432:5432` |
| PG Password | `CHANGE_ME` | `odd_oddities_dev` | `odd_oddities_dev` |
| OpenRouter ApiKey | — | `<sua-chave>` | — |
| Meta tokens | — | `<valores>` | — |
| TokenEncryption | — | `<chave-32>` | — |

---

## Troubleshooting

| Sintoma | Causa | Solucao |
|---|---|---|
| `docker daemon not running` | Docker Desktop fechado | Abrir Docker Desktop |
| `connection refused` PG | PG nao esta rodando | `docker compose -f docker-compose.dev.yml up -d` |
| `password authentication failed` | Senha nao bate | `dotnet user-secrets list` — verificar ConnectionStrings |
| `MinIO Endpoint is empty` | Endpoint nao configurado | Verificar `appsettings.Development.json`: `"Endpoint": "https://s3.binaryten.com.br"` |
| `Access Key does not exist` | Access Key invalido | Verificar user secrets: `dotnet user-secrets list` |
| `SignatureDoesNotMatch` | Secret Key invalido | Re-gerar Access Key no Console MinIO |
| `Access Denied` | Policy nao anexada | Re-criar Access Key com policy `odd-oddities-dev` |

---

## Referencias

- [architecture.md — Configuration Layering](./architecture.md#configuration-layering-regra-obrigatoria)
- [ADR-005 MinIO Privado com Nginx HTTPS](./adr/ADR-005-metadados-armazenamento-minio.md)
- [vps-infra setup guide](../../vps-infra/docs/setup-guide.md)
