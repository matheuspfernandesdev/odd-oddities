# Architecture Decision Record - MinIO Privado com Nginx HTTPS e URLs Pre-assinadas

## Status

Aceito

## Contexto

A Instagram Graph API exige que o conteudo de midia esteja acessivel publicamente por HTTPS para download durante a publicacao. Por outro lado, manter o bucket publicamente listavel expõe todo o acervo a terceiros. A VPS e umica e o projeto nao pode arcar com um CDN.

## Decisao

Manter o **bucket MinIO privado**, exposto ao publico exclusivamente por um **reverse proxy Nginx** com TLS via Let's Encrypt, e gerar **URLs pre-assinadas com validade de 24 horas** para uso da Meta.

- Container MinIO escuta apenas na rede Docker interna (porta 9000).
- Container Nginx expoe o endpoint S3 em `https://storage.<dominio>` com certificado valido.
- Console administrativo do MinIO (porta 9001) **nao** e exposto.
- O Worker gera URL pre-assinada apontando para o dominio publico HTTPS.
- Apos 24 horas a URL expira e o objeto permanece armazenado para o acervo.

## Consequencias

**Positivas**

- Bucket permanece privado.
- URLs temporarias reduzem superficie de ataque.
- Apenas objetos que serao efetivamente publicados sao expostos temporariamente.
- Console do MinIO nao fica exposto na internet.

**Negativas**

- Requer Nginx configurado e certificado valido.
- Renovacao do certificado precisa ser monitorada.
- Configuracao do cliente S3 deve usar o endpoint publico para que a assinatura seja valida no Nginx.

## Alternativas consideradas

- **Bucket publico**: rejeitada pela exposicao completa do acervo.
- **URL pre-assinada direto pelo IP da VPS**: rejeitada pela falta de HTTPS valido e ameaca de bloqueio pela Meta.
- **CDN externo (Cloudflare, BunnyCDN)**: rejeitado pelo custo e complexidade para um projeto pessoal.
