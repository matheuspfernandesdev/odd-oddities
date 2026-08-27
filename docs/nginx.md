# Tutorial: Nginx + Let's Encrypt + Certbot (Ubuntu LTS)

Este tutorial cobre do zero a configuracao de **Nginx como reverse proxy HTTPS** para o MinIO, com certificados **Let's Encrypt** emitidos e renovados via container **Certbot**. Foi desenhado para Ubuntu Server 22.04 LTS ou 24.04 LTS em uma VPS unica com Docker e Docker Compose.

> Pre-requisitos:
>
> - VPS Ubuntu LTS com IP publico.
> - Docker 24+ e Docker Compose v2.
> - Dominio com DNS configuravel.
> - DNS `A` apontando para o IP da VPS ja propagado.
> - Porta 80 e 443 liberadas no firewall.

---

## 1. Estrutura esperada no repositorio

```text
nginx/
├── Dockerfile
├── nginx.conf
└── conf.d/
    └── storage.conf

certbot/
├── Dockerfile
└── scripts/
    ├── init-cert.sh
    └── renew.sh

docker-compose.yml
.env
```

---

## 2. Criar o arquivo `.env`

```text
DOMAIN=storage.exemplo.com
EMAIL=voce@exemplo.com
```

---

## 3. Dockerfile do Nginx

```dockerfile
nginx/Dockerfile
---
FROM nginx:1.27-alpine

RUN apk add --no-cache curl
COPY nginx.conf /etc/nginx/nginx.conf
COPY conf.d /etc/nginx/conf.d
```

---

## 4. nginx.conf

```nginx
nginx/nginx.conf
---
user  nginx;
worker_processes  auto;

error_log  /var/log/nginx/error.log warn;
pid        /var/run/nginx.pid;

events {
    worker_connections  4096;
}

http {
    include       /etc/nginx/mime.types;
    default_type  application/octet-stream;

    log_format  main  '$remote_addr - $remote_user [$time_local] "$request" '
                      '$status $body_bytes_sent "$http_referer" '
                      '"$http_user_agent" "$http_x_forwarded_for"';

    access_log  /var/log/nginx/access.log  main;

    sendfile        on;
    tcp_nopush      on;
    tcp_nodelay     on;
    keepalive_timeout  65;
    types_hash_max_size 2048;
    server_tokens off;

    gzip on;
    gzip_types text/plain text/css application/json application/javascript image/svg+xml;
    gzip_min_length 1024;

    include /etc/nginx/conf.d/*.conf;
}
```

---

## 5. Configuracao do reverse proxy

```nginx
nginx/conf.d/storage.conf
---
server {
    listen 80;
    server_name ${DOMAIN};

    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl http2;
    server_name ${DOMAIN};

    ssl_certificate     /etc/letsencrypt/live/${DOMAIN}/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/${DOMAIN}/privkey.pem;

    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 1d;

    add_header Strict-Transport-Security "max-age=31536000" always;
    add_header X-Content-Type-Options nosniff;
    add_header X-Frame-Options DENY;

    client_max_body_size 50m;
    proxy_read_timeout 60s;
    proxy_send_timeout 60s;

    location / {
        proxy_pass http://minio:9000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;

        proxy_http_version 1.1;
        proxy_set_header Connection "";

        chunked_transfer_encoding off;
    }
}
```

---

## 6. Dockerfile do Certbot

```dockerfile
certbot/Dockerfile
---
FROM certbot/certbot:v2.11.0
COPY scripts /opt/scripts
RUN chmod +x /opt/scripts/*.sh
```

---

## 7. Script de emissao inicial

```bash
certbot/scripts/init-cert.sh
---
#!/usr/bin/env bash
set -euo pipefail

DOMAIN=${DOMAIN:?DOMAIN is required}
EMAIL=${EMAIL:?EMAIL is required}

docker compose run --rm --entrypoint "" certbot \
  certonly --webroot --webroot-path /var/www/certbot \
  --email "$EMAIL" --agree-tos --no-eff-email \
  -d "$DOMAIN"
```

---

## 8. Script de renovacao

```bash
certbot/scripts/renew.sh
---
#!/usr/bin/env bash
set -euo pipefail

docker compose run --rm --entrypoint "" certbot renew

docker compose exec nginx nginx -s reload
```

---

## 9. docker-compose.yml (trecho relevante)

```yaml
services:
  minio:
    image: minio/minio:RELEASE.2024-12-18T13-15-44Z
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: ${MINIO_ACCESS_KEY}
      MINIO_ROOT_PASSWORD: ${MINIO_SECRET_KEY}
    volumes:
      - minio_data:/data
    networks:
      - internal
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 30s
      timeout: 5s
      retries: 5

  nginx:
    build: ./nginx
    depends_on:
      - minio
    ports:
      - "80:80"
      - "443:443"
    environment:
      - DOMAIN=${DOMAIN}
    volumes:
      - ./certbot/www:/var/www/certbot:ro
      - ./certbot/conf:/etc/letsencrypt:ro
    networks:
      - internal
      - edge
    restart: unless-stopped

  certbot:
    build: ./certbot
    entrypoint: "/bin/sh -c 'trap exit TERM; while :; do sleep 12h & wait $${!}; done'"
    volumes:
      - ./certbot/www:/var/www/certbot:rw
      - ./certbot/conf:/etc/letsencrypt:rw

volumes:
  minio_data:

networks:
  internal:
  edge:
```

---

## 10. Primeira execucao

```bash
# 1. Subir MinIO e Nginx
docker compose up -d minio nginx

# 2. Emitir certificado
bash certbot/scripts/init-cert.sh

# 3. Recarregar Nginx
docker compose exec nginx nginx -s reload

# 4. Testar
curl -I https://${DOMAIN}/minio/health/live
```

A primeira requisicao deve retornar `200 OK` com o redirecionamento para HTTPS.

---

## 11. Renovacao automatica

Crie um job no GitHub Actions ou um cron no host:

```cron
0 3 * * * cd /opt/odd-oddities && bash certbot/scripts/renew.sh >> /var/log/odd-oddities-renew.log 2>&1
```

A renovacao ocorre automaticamente a cada 60 dias. O `nginx -s reload` reaplica o certificado.

---

## 12. Manter o console do MinIO privado

O console (porta `9001`) **nao** deve ser exposto publicamente. Nunca adicione `9001:9001` em `ports:`. Para acessar via SSH:

```bash
ssh -L 9001:localhost:9001 usuario@vps
# Em outro terminal:
docker compose port minio 9001
# Acesse em http://localhost:9001
```

---

## 13. Validacao final

- `https://${DOMAIN}/minio/health/live` retorna 200.
- Certificado valido no navegador (cadeia Let's Encrypt).
- Aplicacao gera URL pre-assinada apontando para `${DOMAIN}`.
- Meta baixa a imagem via HTTPS sem erros.

---

## 14. Troubleshooting

| Sintoma | Causa provavel | Solucao |
|---|---|---|
| 502 Bad Gateway | MinIO nao subiu | `docker compose logs minio` |
| 404 em /.well-known | Volume certbot nao montado | Verificar volumes em docker-compose.yml |
| Certificado nao renova | Falha de DNS ou firewall | `dig +short ${DOMAIN}`, verificar 80/443 |
| Console exposto | Porta 9001 publicada | Remover mapeamento em `ports:` |
| Conexao resetada pelo Meta | URL pre-assinada invalida | Conferir `MINIO_PUBLIC_ENDPOINT` |
| Curl retorna 301 para HTTPS | Esperado na porta 80 | Forcar `-L` no curl |
| Nginx reinicia em loop | Erro de syntax | `docker compose exec nginx nginx -t` |
