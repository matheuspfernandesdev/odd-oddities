# Tutorial: Instagram Graph API - Passo a Passo do Zero

Este tutorial cobre todo o caminho necessario para configurar o **Instagram Graph API** para um perfil **Business** ou **Creator**, gerar um **token de longa duracao** e manter o acesso funcionando para o Worker do Odd Oddities.

---

## 1. Pre-requisitos

- Conta pessoal no Instagram.
- Pagina no Facebook (a conta do Instagram sera vinculada a ela).
- Perfil do Instagram convertido para **Business** ou **Creator**.
- Conta de desenvolvedor na Meta (mesma conta do Facebook).

---

## 2. Converter o Instagram para Business/Creator

1. Abra o Instagram no celular.
2. Va em **Configuracoes > Conta > Trocar para conta profissional**.
3. Escolha **Empresa** ou **Criador de conteudo**.

**Importante:** O Instagram **nao obriga** a vincular uma Pagina do Facebook neste momento. O vinculo sera feito manualmente no proximo passo.

---

## 3. Criar Pagina no Facebook (se ainda nao existir)

1. Acesse https://www.facebook.com/pages/create.
2. Escolha **Empresa ou marca** ou **Comunidade ou figura publica**.
3. Preencha:
   - Nome da pagina (ex: "Odd Oddities").
   - Categoria (ex: "Entretenimento").
   - Descricao curta.
4. Clique em **Criar pagina**.

**Nota:** Voce precisa de uma conta no Facebook para criar a pagina. Use a mesma conta que sera usada no Meta for Developers.

---

## 4. Vincular Instagram a Pagina do Facebook

**Atencao:** O caminho mudou! Nao e mais em "Configuracoes".

1. Abra o Instagram no celular.
2. Toque no seu **perfil** (icone embaixo a direita).
3. Toque em **"Editar perfil"**.
4. Role para baixo ate **"Informacoes comerciais publicas"** (se for conta comercial) ou **"Informacoes do perfil"** (se for criador).
5. Toque em **"Pagina"**.
6. Toque em **"Conectar ou criar"**.
7. Toque em **"Entrar no Facebook"** e faca login com a conta que criou a pagina.
8. Escolha a pagina e toque em **"Conectar"**.

---

## 5. Criar App no Meta for Developers

1. Acesse https://developers.facebook.com/apps/creation/.
2. Clique em **Criar um app**.
3. Em **Caso de uso**, selecione **Gerenciar mensagens e conteudo no Instagram**.
4. Em **Tipo de app**, selecione **Business**.
5. Preencha:
   - Nome do app (ex: "Odd Oddities Automation").
   - Email de contato.
   - Conta comercial (Business Manager) - opcional nesta etapa.
6. Clique em **Criar app**.
7. Confirme a senha do Facebook.

**Nota:** Ao escolher o caso de uso "Gerenciar mensagens e conteudo no Instagram", o produto Instagram Graph API e adicionado automaticamente ao app.

---

## 6. Adicionar a conta do Instagram como Tester

1. No menu lateral esquerdo, clique em **"Funcoes do app"** (icone de pessoa).
2. Clique em **"Funcoes"**.
3. No canto superior direito, clique em **"Adicionar pessoas"**.
4. Na janela que abrir, selecione a aba **"Instagram testers"** (nao "Testadores" comum).
5. Digite seu **nome de usuario do Instagram** (sem o @).
6. Selecione a conta correta e envie o convite.
7. O status ficara como "Pending" ate o convite ser aceito.

---

## 7. Aceitar o convite no Instagram

**Atencao:** O convite **nao aparece no aplicativo do celular**! So funciona no navegador do computador.

1. Abra o navegador do **computador**.
2. Acesse https://www.instagram.com.
3. Faca login na conta que voce adicionou como tester.
4. Clique no icone de engrenagem (ou va em **Configuracoes**).
5. Va em **Permissoes do site > Apps e websites**.
6. Clique em **"Convites do Testador"**.
7. Aceite o convite do app "Odd Oddities Automation".

Depois que aceitar, volte ao painel do Meta e o status deve mudar de "Pending" para "Active".

---

## 8. Obter App ID e App Secret

1. No painel do app, va em **Configuracoes > Basico**.
2. Copie:
   - **ID do aplicativo** (sera `META_APP_ID`).
   - **Chave Secreta do aplicativo** (clique em "Mostrar" e copie; sera `META_APP_SECRET`).
3. Armazene ambos em local seguro (GitHub Actions Secrets).

---

## 9. Gerar Token de Acesso e Obter Instagram User ID

O fluxo antigo usava o Graph API Explorer, mas agora e mais direto pelo painel do app.

### Via "Casos de uso" (fluxo novo e recomendado)

1. No menu lateral, clique em **"Casos de uso"**.
2. Clique em **"Personalizar"** ao lado de "Gerenciar mensagens e conteudo no Instagram".
3. Procure a secao **"Configuracao da API com login do Instagram"**.
4. Expanda o passo **"2. Gerar tokens de acesso"**.
5. Clique em **"Adicionar conta"** ou **"Generate access token"**.
6. Faca login com a conta do Instagram Business.
7. O token sera gerado e a resposta JSON mostrara:

```json
{
  "access_token": "IGQV...",
  "user_id": 17841401234567890
}
```

8. **Copie o `access_token`** (sera usado como `META_ACCESS_TOKEN` temporario).
9. **Copie o `user_id`** (sera usado como `INSTAGRAM_USER_ID`).

**Nota:** O token gerado pelo painel e de **curta duracao** (cerca de 1 hora). Para uso em producao, voce precisa troca-lo por um token de longa duracao (60 dias) no proximo passo.

---

## 10. Trocar token curto por token longo

O token gerado no passo anterior dura apenas 1 hora. Para obter um token de 60 dias, faca essa chamada:

```text
GET https://graph.instagram.com/access_token
  ?grant_type=ig_exchange_token
  &client_secret=<APP_SECRET>
  &access_token=<TOKEN_CURTO>
```

Resposta:

```json
{
  "access_token": "<TOKEN_LONGO>",
  "token_type": "bearer",
  "expires_in": 5183944
}
```

`expires_in` e aproximadamente 60 dias. Esse token e o `META_ACCESS_TOKEN` inicial para producao.

---

## 11. Definir escopo das permissoes

Para esta POC, as permissoes necessarias sao:

- `instagram_business_basic`
- `instagram_business_content_publish`

A Meta pode exigir **App Review** para permissoes avancadas. Para uso pessoal publicando apenas na propria conta Business, normalmente o acesso funciona com o token gerado via fluxo acima.

---

## 12. Testar o token

```text
GET https://graph.instagram.com/me?fields=id,username&access_token=<TOKEN_LONGO>
```

Resposta esperada:

```json
{
  "id": "17841401234567890",
  "username": "oddoddities"
}
```

---

## 13. Testar publicacao manual

```text
POST https://graph.facebook.com/v17.0/<INSTAGRAM_USER_ID>/media
  ?image_url=<URL_PUBLICA_HTTPS>
  &caption=Hello world
  &access_token=<TOKEN_LONGO>
```

Resposta:

```json
{
  "id": "<CREATION_ID>"
}
```

Publicar:

```text
POST https://graph.facebook.com/v17.0/<INSTAGRAM_USER_ID>/media_publish
  ?creation_id=<CREATION_ID>
  &access_token=<TOKEN_LONGO>
```

Acompanhar status:

```text
GET https://graph.facebook.com/v17.0/<CREATION_ID>?fields=status_code&access_token=<TOKEN_LONGO>
```

`status_code = PUBLISHED` significa sucesso.

---

## 14. Renovacao automatica (implementada no Worker)

O Worker verifica a data de expiracao e chama:

```text
GET https://graph.instagram.com/refresh_access_token
  ?grant_type=ig_refresh_token
  &access_token=<TOKEN_LONGO_ATUAL>
```

Resposta:

```json
{
  "access_token": "<NOVO_TOKEN_LONGO>",
  "token_type": "bearer",
  "expires_in": 5183944
}
```

O Worker criptografa o novo token com AES-256-GCM e substitui o anterior.

**Regras oficiais:**

- O token precisa ter mais de 24 horas.
- O token nao pode estar expirado.
- Renovacoes bem-sucedidas adicionam mais 60 dias.
- Se revogado (por senha, logout ou revogacao manual), exige novo fluxo OAuth.

---

## 15. Reautorizacao manual (quando a renovacao automatica falha)

1. Repita os passos 9 e 10 para gerar um novo token.
2. Defina a variavel de ambiente `META_ACCESS_TOKEN` com o novo valor.
3. Execute o deploy novamente para injetar a variavel.
4. O Worker detectara o novo token na proxima execucao.

---

## 16. Troubleshooting

| Sintoma | Causa provavel | Solucao |
|---|---|---|
| `OAuthException 190` | Token expirado ou invalido | Reautorizar via fluxo OAuth |
| `OAuthException 100` | Permissao nao concedida | Revisar escopos e App Review |
| `OAuthException 10` | App nao tem permissao para a conta | Verificar vinculo Instagram x Pagina |
| Imagem nao aparece | URL publica nao acessivel | Verificar HTTPS e URL pre-assinada |
| `media_publish` retorna `IN_PROGRESS` | Processamento assincrono | Polling ate virar `PUBLISHED` ou `ERROR` |
| Token refresh retorna 400 | Token ainda muito novo (<24h) | Aguardar 24h ou usar novo token |
| Convite de tester nao aparece | Tentando aceitar no app do celular | Usar navegador do PC em instagram.com |
| Dropdown de permissoes vazio no Graph API Explorer | Token nao gerado ainda | Gerar token primeiro antes de adicionar permissoes |
| App "nao disponivel" ao gerar token | App sem permissoes configuradas | Usar fluxo "Casos de uso > Personalizar" em vez do Graph API Explorer |

---

## 17. Referencias oficiais

- https://developers.facebook.com/docs/instagram-platform
- https://developers.facebook.com/docs/instagram-platform/reference/access_token
- https://developers.facebook.com/docs/instagram-platform/reference/refresh_access_token
- https://developers.facebook.com/docs/instagram-api/reference/media
- https://developers.facebook.com/docs/instagram-api/reference/media-publish
- https://developers.facebook.com/documentation/instagram-platform/create-an-instagram-app
