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
4. Conecte a uma Pagina do Facebook.
5. Se nao existir Pagina, crie uma neste passo.

---

## 3. Criar Pagina no Facebook (se ainda nao existir)

1. Acesse https://www.facebook.com/pages/create.
2. Escolha **Empresa ou marca** ou **Comunidade ou figura publica**.
3. Preencha:
   - Nome da pagina (ex: "Odd Oddities").
   - Categoria (ex: "Entretenimento").
   - Descricao curta.
4. Clique em **Criar pagina**.

---

## 4. Vincular Instagram a Pagina do Facebook

1. Abra o Instagram no celular.
2. Va em **Configuracoes > Conta > Conta profissional > Pagina conectada**.
3. Selecione a pagina criada.
4. Confirme o vinculo.

---

## 5. Criar App no Meta for Developers

1. Acesse https://developers.facebook.com/apps/creation/.
2. Clique em **Criar um app**.
3. Em **Caso de uso**, selecione **Outro**.
4. Em **Tipo de app**, selecione **Business**.
5. Preencha:
   - Nome do app (ex: "Odd Oddities Automation").
   - Email de contato.
   - Conta comercial (Business Manager) - opcional nesta etapa.
6. Clique em **Criar app**.
7. Confirme a senha do Facebook.

---

## 6. Adicionar o produto Instagram

1. No painel do app, procure por **Instagram Graph API**.
2. Clique em **Configurar**.
3. Aceite os termos.

---

## 7. Adicionar a conta do Instagram como Tester

1. Va em **Funcoes > Funcoes do app**.
2. Em **Testers do Instagram**, clique em **Adicionar testers do Instagram**.
3. Pesquise pelo seu usuario do Instagram.
4. Envie o convite.

---

## 8. Aceitar o convite no Instagram

1. Abra o Instagram no celular.
2. Va em **Configuracoes > Empresa > Configurar contas > Convites**.
3. Aceite o convite do app criado.
4. Autorize as permissoes solicitadas.

---

## 9. Obter App ID e App Secret

1. No painel do app, va em **Configuracoes > Basico**.
2. Copie:
   - **App ID** (sera `META_APP_ID`).
   - **App Secret** (clique em "Mostrar" e copie; sera `META_APP_SECRET`).
3. Armazene ambos em local seguro (GitHub Actions Secrets).

---

## 10. Obter Instagram User ID

O Instagram User ID e necessario para todas as chamadas a Instagram Graph API.

### Via Graph API Explorer

1. Acesse https://developers.facebook.com/tools/explorer/.
2. Selecione o app criado.
3. Adicione a permissao `instagram_business_basic`.
4. Gere um token curto de teste (clique em **Generate Access Token**).
5. Faca a chamada:

```text
GET https://graph.facebook.com/v17.0/me/accounts?access_token=<TOKEN>
```

6. Localize a Pagina do Odd Oddities e copie o `id` (Page ID).
7. Agora obtenha o Instagram User ID:

```text
GET https://graph.facebook.com/v17.0/<PAGE_ID>?fields=instagram_business_account&access_token=<TOKEN>
```

8. O campo `instagram_business_account.id` e o seu **Instagram User ID** (`INSTAGRAM_USER_ID`).

---

## 11. Gerar token de curta duracao (para troca)

A primeira vez, voce precisa de um token curto gerado por OAuth. Para isso, monte a URL abaixo substituindo `<APP_ID>`, `<REDIRECT_URI>` e nosso escopo:

```text
https://api.instagram.com/oauth/authorize
  ?client_id=<APP_ID>
  &redirect_uri=<REDIRECT_URI>
  &scope=user_profile,user_media
  &response_type=code
```

`<REDIRECT_URI>` deve estar cadastrado em **Instagram > API Setup with Instagram Business Login > Valid OAuth Redirect URIs** no painel do app.

1. Abra a URL no navegador.
2. Faca login com a conta do Instagram Business.
3. Aceite as permissoes.
4. O Instagram redireciona para `<REDIRECT_URI>?code=<CODIGO>`.
5. Copie o valor de `code`.

---

## 12. Trocar code por token curto

```text
POST https://api.instagram.com/oauth/access_token
  ?client_id=<APP_ID>
  &client_secret=<APP_SECRET>
  &grant_type=authorization_code
  &redirect_uri=<REDIRECT_URI>
  &code=<CODE>
```

Resposta:

```json
{
  "access_token": "<TOKEN_CURTO>",
  "user_id": <INSTAGRAM_USER_ID>
}
```

`access_token` dura cerca de 1 hora.

---

## 13. Trocar token curto por token longo

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

`expires_in` e aproximadamente 60 dias. Esse token e o `META_ACCESS_TOKEN` inicial.

---

## 14. Definir escopo das permissoes

Para esta POC, as permissoes necessarias sao:

- `instagram_business_basic`
- `instagram_business_content_publish`

A Meta pode exigir **App Review** para permissoes avancadas. Para uso pessoal publicando apenas na propria conta Business, normalmente o acesso funciona com o token gerado via fluxo OAuth acima.

---

## 15. Testar o token

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

## 16. Testar publicacao manual

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

## 17. Renovacao automatica (implementada no Worker)

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

## 18. Reautorizacao manual (quando a renovacao automatica falha)

1. Repita os passos 11 a 13 para gerar um novo token.
2. Defina a variavel de ambiente `META_ACCESS_TOKEN` com o novo valor.
3. Execute o deploy novamente para injetar a variavel.
4. O Worker detectara o novo token na proxima execucao.

---

## 19. Troubleshooting

| Sintoma | Causa provavel | Solucao |
|---|---|---|
| `OAuthException 190` | Token expirado ou invalido | Reautorizar via fluxo OAuth |
| `OAuthException 100` | Permissao nao concedida | Revisar escopos e App Review |
| `OAuthException 10` | App nao tem permissao para a conta | Verificar vinculo Instagram x Pagina |
| Imagem nao aparece | URL publica nao acessivel | Verificar HTTPS e URL pre-assinada |
| `media_publish` retorna `IN_PROGRESS` | Processamento assincrono | Polling ate virar `PUBLISHED` ou `ERROR` |
| Token refresh retorna 400 | Token ainda muito novo (<24h) | Aguardar 24h ou usar novo token |

---

## 20. Referencias oficiais

- https://developers.facebook.com/docs/instagram-platform
- https://developers.facebook.com/docs/instagram-platform/reference/access_token
- https://developers.facebook.com/docs/instagram-platform/reference/refresh_access_token
- https://developers.facebook.com/docs/instagram-api/reference/media
- https://developers.facebook.com/docs/instagram-api/reference/media-publish
