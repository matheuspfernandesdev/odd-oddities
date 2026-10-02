# Architecture Decision Record - Clientes Separados para Texto e Imagem no OpenRouter

## Status

Aceito

## Contexto

O OpenRouter expoe dois endpoints distintos para geracao multimodal:

- `POST /api/v1/chat/completions` para texto (com suporte opcional a multimodalidade).
- `POST /api/v1/images` para geracao de imagem dedicada.

A documentacao oficial recomenda o endpoint dedicado de imagens para cenarios text-to-image. Tentar tratar ambos por meio do mesmo adapter adicionaria complexidade desnecessaria e misturaria contratos.

## Decisao

Adotar **dois clientes separados** dentro do adapter `OpenRouterAdapter`:

- `OpenRouterTextClient`: usa `POST /api/v1/chat/completions` com `response_format=json_schema`.
- `OpenRouterImageClient`: usa `POST /api/v1/images` e recebe a imagem como `b64_json`.

Ambos compartilham a mesma API key e base URL `https://openrouter.ai/api/v1`.

## Consequencias

**Positivas**

- Contratos explicitos e segregados.
- Troca individual de cada cliente sem afetar o outro.
- Tratamento de erros especifico para cada caso.
- Logs estruturados segregados por tipo.

**Negativas**

- Mais arquivos de adapter.
- Duplicacao de configuracao (base URL, headers).

## Alternativas consideradas

- **Cliente unificado via `/chat/completions`**: rejeitada pelo formato multimodal misto e risco de quebrar quando o provedor alterar o retorno.
- **SDK OpenRouter .NET**: nao adotado para manter baixo acoplamento com a API HTTP.
