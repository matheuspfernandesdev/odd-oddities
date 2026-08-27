# Architecture Decision Record - Retry com Backoff Exponencial para Erros Transitorios

## Status

Aceito

## Contexto

O OpenRouter e a Meta Graph API podem retornar erros transitorios por timeout, rate-limit ou problemas temporarios de infraestrutura. Falhas por erro de validacao ou rejeicao por similaridade nao devem ser repetidas, pois o resultado nao mudaria.

## Decisao

Aplicar **retry com backoff exponencial** somente para integracoes externas (OpenRouter e Meta Graph API), nas seguintes condicoes:

- Timeout de rede.
- Erros de conexao.
- HTTP `408` Request Timeout.
- HTTP `429` Too Many Requests.
- HTTP `5xx`.

Parametros:

- Maximo de **3 tentativas**.
- Intervalo inicial: **10 segundos**.
- Multiplicador: **2**.
- Limite maximo: **120 segundos**.
- Apos exaurir: registrar `FailureStep` correspondente e aguardar o proximo horario.

Erros de validacao (similaridade, fonte invalida, JSON malformado) **nao** usam retry.

## Consequencias

**Positivas**

- Resiliencia a falhas transitorias.
- Aumento da taxa de sucesso do pipeline.
- Logs estruturados registrando cada tentativa.

**Negativas**

- Janela maxima de espera de ate 4 minutos (10 + 20 + 40 com cap em 120).
- Possivel sobreposicao com a proxima execucao agendada em caso de pico.

## Alternativas consideradas

- **Retry sem backoff**: rejeitada por amplificar picos.
- **Polly com circuit breaker**: rejeitada pela complexidade e pelo baixo volume.
- **Sem retry**: rejeitada por perda desnecessaria de tentativas em falhas transitorias.
