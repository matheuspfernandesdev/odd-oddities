# Revisão de Arquitetura e Organização de Código (sem mexer em regras de negócio)

## Objetivo

Reorganizar a estrutura do `OddOddities` mantendo **todas as regras de negócio (BR-001..BR-014), contratos de portas, comportamento de adapters, formatos de log, e observabilidade**. Apenas mudar como o código está **organizado** (pastas, responsabilidades, dependências, fronteiras de camadas).

Resultado esperado: o `dotnet build` continua passando sem mudanças funcionais, o pipeline continua publicando o mesmo `Post`/`Publication` com o mesmo `Status`, e a suite atual de unit tests (mesmo que vazia) continua compilando.

## Fora de escopo

- Implementar testes novos (apenas reorganizar a estrutura para recebê-los).
- Alterar validações, thresholds, regras de quota, mensagens de erro, códigos de erro, status de `Post`, lógica do `SimilarityCheckService`, `ScheduleService`, regras de retry, ou qualquer campo persistido.
- Mexer em migrations / seed.
- Trocar bibliotecas externas (Polly, ImageSharp, AWSSDK.S3, Serilog, Npgsql, EF Core).

## Diagnóstico resumido (achados que motivam o plano)

1. **Layers misturadas em `Application/Services`** — pasta `Services` reúne:
   - Ports/adapters de domínio (CategorySelectionService, SourceValidationService, SimilarityCheckService, ImageSharpProcessingService, PresignedUrlService, TokenRenewalService, ScheduleService) que **devem viver na Infrastructure** ou em uma pasta `Ports` separada, porque o resto do app as vê só pela interface.
   - Steps de pipeline (TextGenerationStep, ImageGenerationStep, PublicationStep) que **devem** estar isolados em `Application/Steps`.
   - Um "serviço" técnico (LogCorrelationService) que envolve Serilog e não é regra de negócio — pertence à Infrastructure de logging.

2. **`IPipelineStep` mora no Domain mas só faz sentido para a Application** — interface + `StepResult` + `PipelineExecutionContext` em `Domain/Interfaces/IPipelineStep.cs` vazam conceitos de orquestração para a camada de domínio. Devem migrar para `Application/Pipeline/`.

3. **`Worker` faz papel de middle man / glue code** — o `Worker.RunPipelineAsync` resolve `PipelineOrchestrator` manualmente, cria scope, e ainda passa `0/0/empty` para parâmetros ignorados. Com `IServiceScopeFactory` + `IServiceProvider` correto, isso pode virar uma única chamada de extensão (ou injeção de `IServiceProvider`) e o método `ExecuteAsync` do `PipelineOrchestrator` deve perder os 4 parâmetros "ignorados" (`categoryId`, `subcategoryId`, `categoryName`, `subcategoryName`).

4. **`PipelineOrchestrator.ParseFailureStep` / `MapExceptionToFailureStep` — código duplicado** — dois `switch` quase idênticos que mapeiam string → `FailureStep` (um devolve enum, outro devolve string). Devem virar um único mapa `IReadOnlyDictionary<string, FailureStep>` e uma única função.

5. **Mapeamento de `FailureStep` por string em vez de tipo** — `StepResult.FailureStep` é `string` em vez de `FailureStep` (enum). Isso força parsing em runtime no orchestrator. Mudar para `FailureStep?` é uma mudança de organização, não de regra de negócio: a *string* de log e o *enum* continuam existindo lado a lado, só a fonte da verdade vira o enum.

6. **`PipelineExecutionContext` é uma god object** — 14 campos públicos, alguns mutáveis, alguns só leitura na prática. Cada step lê e escreve em campos diferentes. Precisa virar `record` imutável + **sub-contexts** por step (`TextContext`, `ImageContext`, `PublicationContext`) ou, no mínimo, um construtor que garanta imutabilidade do que vem da seleção de categoria.

7. **Duplicação de `using`/DTOs entre Application e Infrastructure** — adapters no `Infrastructure` referenciam `OddOddities.Application.Services` (e o `ServiceCollectionExtensions` também), o que vira dependência circular quando algum "service" precisar ir para `Application`. Ex.: `ScheduleService` é registrado em `ServiceCollectionExtensions` mas vive em `Application/Services`. Aplicar a separação abaixo desfaz esse nó.

8. **Constants soltas em classes** — `MaxGenerationAttempts = 3`, `MaxPollingAttempts = 30`, `PollingIntervalSeconds = 2`, `RenewalThresholdDays = 14`, `MinTokenLength = 3`, `PresignedUrlExpiry` etc. estão espalhadas em classes como `private const`. Sem mexer no valor, mover para `Domain/Constants/` (ou `Application/Constants/`) deixa a localização previsível.

9. **`OpenRouterConfiguration` mistura config de texto e imagem** — e a config de `MinioConfiguration` mistura endpoint interno, endpoint público e quota. São agregados diferentes. Quebrar em `OpenRouterTextOptions`/`OpenRouterImageOptions` e `MinioInternalOptions`/`MinioPublicOptions` ajuda o `IOptions<>` injection e remove a violação atual (OpenRouter text e image adapters recebendo o mesmo `OpenRouterConfiguration`).

10. **`Middleware/GlobalExceptionMiddleware` está no Worker** — a `GlobalExceptionMiddleware` é infraestrutura de aplicação (registrada em `Program.cs`) e não pertence ao projeto Host. Deve ir para `Infrastructure/Middleware/` (ou `Application/`) para que qualquer host futuro (Web API) possa reusar.

11. **Sem `Application/Abstractions/` nem `Application/Common/`** — o `PipelineExecutionContext` e o `StepResult` precisam de um lar claro. Padronizar em `Application/Pipeline/` (e `Application/Abstractions/` para reusáveis como `Clock`).

12. **Worker tem `appsettings.json` com segredo padrão** — pequeno cheiro: o arquivo versionado já tem connection string de exemplo. **Não vou mexer no segredo** (fora de escopo), apenas citar como follow-up.

## Estrutura alvo

```
src/
├── OddOddities.Domain/                       (sem mudanças funcionais; só realocação de tipos)
│   ├── Entities/
│   ├── Enums/
│   ├── Exceptions/
│   ├── Interfaces/                           (APENAS contratos: IPostRepository, IClock, ITextGenerationPort, ...)
│   ├── ValueObjects/                         (AppConfiguration e sub-configs quebrados)
│   └── Constants/                            (NOVO: MagicNumbers.cs com os consts extraídos)
│
├── OddOddities.Application/
│   ├── Abstractions/                         (NOVO: Clock, IDateTimeProvider, LogContextScope)
│   ├── Pipeline/                             (NOVO lar para orquestração)
│   │   ├── IPipelineStep.cs                  (MOVIDO de Domain/Interfaces)
│   │   ├── PipelineStep.cs                   (base abstrata opcional para remover boilerplate)
│   │   ├── PipelineContext.cs                (record imutável; sub-contexts por step)
│   │   ├── StepResult.cs                     (FailureStep vira enum?)
│   │   └── PipelineOrchestrator.cs           (movido de Services/)
│   ├── Steps/                                (NOVO; substitui Services/ para os steps)
│   │   ├── TextGenerationStep.cs
│   │   ├── ImageGenerationStep.cs
│   │   └── PublicationStep.cs
│   ├── UseCases/                             (NOVO: extrai CategorySelectionService → SelectBalancedCategoryUseCase)
│   ├── Ports/                                (NOVO: contratos/contratos de Application que não estão em Domain)
│   │   └── ILogCorrelationPort.cs            (movido de Domain/Interfaces)
│   ├── DTOs/                                 (RefreshTokenResponse)
│   └── DependencyInjection/                  (NOVO: AddApplicationServices)
│       └── ApplicationServiceCollectionExtensions.cs
│
├── OddOddities.Infrastructure/
│   ├── Adapters/                             (OpenRouter text/image, MinIO, Meta, repos)
│   ├── Logging/                              (Serilog DestructuringPolicy, LogCorrelationService)
│   ├── Middleware/                           (GlobalExceptionMiddleware MOVIDO do Worker)
│   ├── Data/                                 (DbContext, Configurations, Migrations)
│   ├── Options/                              (NOVO: classes parciais/configs quebradas)
│   │   ├── OpenRouterTextOptions.cs
│   │   ├── OpenRouterImageOptions.cs
│   │   ├── MetaOptions.cs
│   │   ├── MinioOptions.cs                   (sub-seções Internal/Public/Quota)
│   │   └── TokenEncryptionOptions.cs
│   ├── Retry/                                (NOVO lar para Polly policies, ADR-007)
│   │   └── HttpResiliencePipelineFactory.cs
│   └── DependencyInjection/
│       └── InfrastructureServiceCollectionExtensions.cs
│
└── OddOddities.Worker/                      (fica enxuto: Program.cs, Worker.cs, Dockerfile, appsettings*)
    ├── Program.cs                            (chama AddApplicationServices + AddInfrastructureServices + UseMiddleware<GlobalExceptionMiddleware>)
    ├── Worker.cs                             (perde a resolução manual de scope: recebe IServiceProvider, ou melhor, IServiceScopeFactory e chama pipeline.RunAsync(ct))
    ├── HealthChecks/                         (NOVO: separa a configuração de health check do Program.cs)
    ├── Scheduling/                           (NOVO: PeriodicTimerHostedService, se extrairmos)
    ├── appsettings.json
    ├── appsettings.Development.json
    └── Dockerfile

tests/
├── OddOddities.UnitTests/                   (estrutura existente preservada; pasta preparada para novos testes)
│   ├── Application/Steps/                    (NOVO)
│   ├── Application/Pipeline/                 (NOVO)
│   ├── Domain/                               (NOVO; caso venhamos a testar value objects)
│   └── Infrastructure/Adapters/              (NOVO)
```

## Mudanças concretas por arquivo (sem mexer em regra de negócio)

### 1. Domain

- **Mover** `IPipelineStep.cs` (inteiro, com `IPipelineStep`, `StepResult`, `PipelineExecutionContext`) de `Domain/Interfaces/` → `Application/Pipeline/`. Domain não conhece mais pipeline/orquestração.
- **Criar** `Domain/Constants/PipelineConstants.cs` com `MaxGenerationAttempts`, `MaxPollingAttempts`, `PollingIntervalSeconds`, `RenewalThresholdDays`, `MinTokenLength`, `MinioDefaultQuotaBytes`, `PresignedUrlExpiry`, `DefaultSimilarityThreshold`, `DefaultPostCategoryWindowDays`. Reapontar usos (sem mudar valores).
- **Quebrar** `AppConfiguration.cs` em arquivos separados por seção, **mantendo os mesmos nomes JSON** para não quebrar `appsettings.json`:
  - `OpenRouterOptions` (sem mudanças de campo) → opcionalmente quebrar em `OpenRouterTextOptions` + `OpenRouterImageOptions` com seção `OpenRouter:Text` / `OpenRouter:Image` em `appsettings.json`.
  - `MinioOptions` quebrar em `MinioOptions` (com sub-objetos `Internal`/`Public`/`Quota`) — exige atualizar `appsettings.json` na mesma PR.
  - Demais (`MetaOptions`, `ScheduleOptions`, `ImageProcessingOptions`, `TokenEncryptionOptions`, `ConnectionStringsOptions`) viram arquivos próprios, mas expostos ainda via `AppConfiguration` agregador para compatibilidade com `IOptions<AppConfiguration>`.
- **Mover** `ILogCorrelationPort` de `Domain/Interfaces/` → `Application/Ports/` (correlação de log não é regra de domínio).

### 2. Application

- **Criar** `Application/Pipeline/`:
  - `IPipelineStep.cs` (movido do Domain). `StepResult.FailureStep` vira `FailureStep?` (enum) e expõe uma propriedade `FailureStepName` que faz `.ToString()` para o que `StepResult.FailureStep` retornava antes — assim o log estruturado fica idêntico. `PipelineExecutionContext` vira `sealed record` imutável com factories para sub-contexts:
    ```csharp
    public sealed record PipelineContext(
        Guid ExecutionId,
        CategorySelection Selection,
        TextGeneration? Text = null,
        ImageGeneration? Image = null,
        Publication? PublicationStep = null);
    ```
  - `PipelineOrchestrator.cs` (movido) — recebe `IEnumerable<IPipelineStep>` + `ICategorySelectionPort` + `IPostRepository` + `ILogCorrelationPort` + `ILogger`. **Remove** o método `ExecuteAsync(long, long, string, string, CancellationToken)` por uma sobrecarga `ExecuteAsync(CancellationToken)`. Aplica o mapa único `FailureStepMap` (Dicionário estático) e remove `MapExceptionToFailureStep`/`ParseFailureStep` duplicados.
  - Opcional: introduzir `PipelineStepBase` para unificar o try/catch/log do orchestrator (cada step continua podendo fazer validações internas próprias; sem mudar o resultado).
- **Criar** `Application/Steps/` e mover `TextGenerationStep.cs`, `ImageGenerationStep.cs`, `PublicationStep.cs`. Ajustar `using` de `OddOddities.Domain.Interfaces` (idem) e atualizar `StepName` para um `static readonly FailureStep FailureStepOf = ...` que substitui as comparações string→enum.
- **Criar** `Application/UseCases/SelectBalancedCategoryUseCase.cs` envolvendo `CategorySelectionService` (mantido como adapter fino) ou movendo a lógica de seleção que hoje está em `IPostRepository.GetLeastUsedCategoryAsync` (a query continua no repository; só extraímos o use case que o compõe).
- **Criar** `Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`:
  ```csharp
  public static IServiceCollection AddApplicationServices(this IServiceCollection s) =>
      s.AddScoped<ICategorySelectionPort, SelectBalancedCategoryUseCase>()
       .AddScoped<ILogCorrelationPort, LogCorrelationService>()
       .AddScoped<IPipelineStep, TextGenerationStep>()
       .AddScoped<IPipelineStep, ImageGenerationStep>()
       .AddScoped<IPipelineStep, PublicationStep>()
       .AddScoped<PipelineOrchestrator>();
  ```
- **Mover** `LogCorrelationService.cs` de `Application/Services/` → `Infrastructure/Logging/`. Continua implementado por cima de Serilog, mas deixa de ser "Application".

### 3. Infrastructure

- **`Adapters/`**: manter, mas atualizar `using` para os novos caminhos de interfaces e de config.
- **Criar** `Infrastructure/Retry/HttpResiliencePipelineFactory.cs` (ADR-007) que constrói o `ResiliencePipeline` para os três `HttpClient`s (OpenRouter texto/imagem, Meta). Sem mudar tempos, contagens ou estratégia. Os `AddHttpClient<...>` passam a delegar para essa factory.
- **Mover** `GlobalExceptionMiddleware` de `OddOddities.Worker/Middleware/` → `Infrastructure/Middleware/`. Reapontar `Program.cs`.
- **`DependencyInjection/ServiceCollectionExtensions.cs`**:
  - Remover registros de `ICategorySelectionPort`, `ILogCorrelationPort`, `IPipelineStep` (movidos para `AddApplicationServices`).
  - Manter registros de adapters, repos, HttpClients, e o `ISchedulerPort` (segue em Application/UseCases, registrado aqui por ser "infra" de tempo).
  - Renomear para `AddInfrastructureServices` (já está) e adicionar `AddApplicationServices` como chamada separada.

### 4. Worker

- `Program.cs`:
  - Chamar `builder.Services.AddApplicationServices()` antes de `AddInfrastructureServices()`.
  - Mover o `app.MapHealthChecks(...)` para `Worker/HealthChecks/HealthCheckEndpointExtensions.cs`.
  - Mover o `using (var scope = app.Services.CreateScope()) { ... MigrateAsync() }` para `Worker/StartupTasks/ApplyMigrationsHostedService.cs` (`IHostedService` que roda antes do `Worker`).
- `Worker.cs`:
  - Injetar `IServiceScopeFactory` (já injeta) e `IClock` (de `Application/Abstractions/IClock`) para `DateTimeOffset.UtcNow` (substituir `DateTimeOffset.UtcNow` direto).
  - Substituir `RunPipelineAsync` por:
    ```csharp
    private async Task RunPipelineAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<PipelineOrchestrator>();
        await pipeline.ExecuteAsync(ct);
    }
    ```
  - Trocar `DateTime.UtcNow` por `_clock.UtcNow` (criando `SystemClock : IClock` em Infrastructure).
- `Worker/Middleware/` — removido (movido para Infrastructure).

### 5. Tests

- Não adicionar testes nesta rodada (fora de escopo).
- Criar as pastas vazias `tests/OddOddities.UnitTests/Application/Steps/`, `/Application/Pipeline/`, `/Domain/`, `/Infrastructure/Adapters/` com `.gitkeep`. Deixa a árvore pronta para a próxima task.

## Mapeamento de moves (para revisão)

| De | Para |
|---|---|
| `Domain/Interfaces/IPipelineStep.cs` (todo) | `Application/Pipeline/IPipelineStep.cs` |
| `Application/Services/CategorySelectionService.cs` | `Application/UseCases/SelectBalancedCategoryUseCase.cs` |
| `Application/Services/SourceValidationService.cs` | `Infrastructure/Adapters/SourceValidationService.cs` (implementa `ISourceValidationPort`) |
| `Application/Services/SimilarityCheckService.cs` | `Infrastructure/Adapters/SimilarityCheckService.cs` |
| `Application/Services/ImageSharpProcessingService.cs` | `Infrastructure/Adapters/ImageSharpProcessingService.cs` |
| `Application/Services/PresignedUrlService.cs` | `Infrastructure/Adapters/PresignedUrlService.cs` |
| `Application/Services/TokenRenewalService.cs` | `Infrastructure/Adapters/TokenRenewalService.cs` |
| `Application/Services/ScheduleService.cs` | `Application/UseCases/ScheduleService.cs` (mantém em Application porque é "regras de tempo", não I/O) |
| `Application/Services/LogCorrelationService.cs` | `Infrastructure/Logging/LogCorrelationService.cs` |
| `Application/Services/TextGenerationStep.cs` | `Application/Steps/TextGenerationStep.cs` |
| `Application/Services/ImageGenerationStep.cs` | `Application/Steps/ImageGenerationStep.cs` |
| `Application/Services/PublicationStep.cs` | `Application/Steps/PublicationStep.cs` |
| `Application/Services/PipelineOrchestrator.cs` | `Application/Pipeline/PipelineOrchestrator.cs` |
| `Worker/Middleware/GlobalExceptionMiddleware.cs` | `Infrastructure/Middleware/GlobalExceptionMiddleware.cs` |
| `Domain/Interfaces/ILogCorrelationPort.cs` | `Application/Ports/ILogCorrelationPort.cs` |
| Constants espalhadas | `Domain/Constants/PipelineConstants.cs` + `Domain/Constants/StorageConstants.cs` |

## Riscos & mitigações

- **Mover arquivos muda namespaces** → cada move vira um `using` atualizado em todos os arquivos dependentes. Aceitar um PR com diff de namespace-only em 20+ arquivos é esperado; vamos agrupar por camada para reduzir churn de PR.
- **Quebrar `AppConfiguration` pode quebrar `appsettings.json`** → manter o nome da seção raiz `AppConfiguration` e fazer a quebra via sub-objetos com a mesma capitalização usada hoje. Verificar com `dotnet build` após cada bloco.
- **Mover `IPipelineStep` para Application** não introduz dependência circular porque Domain continua sem dependência de Application.
- **Inversão de dependência** — `Infrastructure/Adapters/SourceValidationService` (movido) precisa de `IHttpClientFactory`; o registro do HttpClient continua em `Infrastructure/DependencyInjection`. Nenhuma mudança de comportamento.

## Validação (sem implementar regras de negócio novas)

1. `dotnet restore` em `OddOddities.slnx` resolve sem novos warnings de ciclos.
2. `dotnet build` passa em `Release` e em `Debug`.
3. `dotnet test` continua passando (suite atual é vazia/trivial — não deve regredir).
4. `dotnet format --verify-no-changes` (CI) — sem mudanças de formatação inesperadas.
5. Inspeção manual: `grep -r "Services/" src/` deve voltar vazio para `Application/Services`; `grep -r "Domain/Interfaces/IPipelineStep" src/` deve voltar vazio; nenhum `using OddOddities.Application.Services` em arquivos de `Infrastructure/`.
6. Conferir no log do worker: o JSON de saída do pipeline mantém os mesmos campos `executionId`, `step`, `outcome`, `durationMs`, `postStatus` — sem mudança de schema.
7. Conferir no banco: nenhum migration nova necessária (mudança é só de organização).

## Tarefas ordenadas para o agente implementador

1. Criar `Domain/Constants/` e mover consts. Atualizar `using` em todos os arquivos afetados. `dotnet build`.
2. Mover `IPipelineStep.cs` (todo o arquivo) de `Domain/Interfaces/` → `Application/Pipeline/`. Ajustar `using` em `PipelineOrchestrator`, steps e nos registros. `dotnet build`.
3. Transformar `StepResult.FailureStep` em `FailureStep?` (enum) com propriedade `FailureStepName` que preserva o string. Atualizar `PipelineOrchestrator` (remover `ParseFailureStep`/`MapExceptionToFailureStep` duplicados em favor de um único `IReadOnlyDictionary<string, FailureStep>` estático). Atualizar os 3 steps para passar o enum direto. `dotnet build`.
4. Refatorar `PipelineExecutionContext` em `record` imutável com sub-contexts. Atualizar `PipelineOrchestrator` e os 3 steps. **Sem mudar os valores persistidos** (apenas a forma de carregar o contexto). `dotnet build`.
5. Mover steps (`TextGenerationStep`, `ImageGenerationStep`, `PublicationStep`) para `Application/Steps/`. Mover `PipelineOrchestrator` para `Application/Pipeline/`. Mover `CategorySelectionService` e `ScheduleService` para `Application/UseCases/`. Ajustar `using` em todos os arquivos e registros. `dotnet build`.
6. Mover `SourceValidationService`, `SimilarityCheckService`, `ImageSharpProcessingService`, `PresignedUrlService`, `TokenRenewalService` para `Infrastructure/Adapters/`. Mover `LogCorrelationService` para `Infrastructure/Logging/`. Mover `ILogCorrelationPort` para `Application/Ports/`. Ajustar registros. `dotnet build`.
7. Quebrar `AppConfiguration` em sub-options sem mudar nomes de seção. Atualizar `IOptions<...>` consumers. Atualizar `appsettings.json` e `appsettings.Development.json` se necessário. `dotnet build`.
8. Mover `GlobalExceptionMiddleware` para `Infrastructure/Middleware/`. Mover o registro de health check para `Worker/HealthChecks/`. Mover a aplicação de migrations para um `IHostedService` `Worker/StartupTasks/ApplyMigrationsHostedService.cs`. `dotnet build`.
9. Criar `Application/Abstractions/IClock` e `Infrastructure/Adapters/SystemClock`. Injetar no `Worker` e no `PipelineOrchestrator` (substituir `DateTime.UtcNow` por `_clock.UtcNow` em pontos onde a hora é observável: timestamps de `Post.UpdatedAt`, `PublishedAt`, logs de "next run"). **Atenção**: a `Post.UpdatedAt = DateTime.UtcNow` é regra de negócio; se houver dúvida, manter como está e marcar como follow-up.
10. Renomear `ServiceCollectionExtensions` em Infrastructure para `InfrastructureServiceCollectionExtensions` e criar `ApplicationServiceCollectionExtensions`. Atualizar `Program.cs` para chamar ambos. `dotnet build`.
11. Rodar `dotnet format` e `dotnet test` finais. Commit por etapa.

## Não-objetivos explícitos (para evitar escopo)

- Não criar testes novos.
- Não migrar `MinioConfiguration` para `MinioInternal`/`MinioPublic` (fica para uma task de config dedicada, já que exige editar `appsettings.json` de produção).
- Não introduzir Polly agora (já existe `EnableRetryOnFailure` no EF Core e a AWS SDK tem retry próprio; ADR-007 fica como follow-up se tornar código).
- Não mexer em Docker, Nginx, Meta, ou qualquer adapter externo além de reorganizar.
- Não renomear entidades, enums, ou campos de banco.

## Follow-ups (fora deste plano, listados para registro)

- Quebrar `MinioConfiguration` em `MinioInternalOptions` / `MinioPublicOptions` / `MinioQuotaOptions` (afeta `appsettings.json`).
- Adicionar `IClock` em todos os pontos que hoje usam `DateTime.UtcNow` (mantidos fora do escopo para evitar mudar timestamps persistidos).
- Adicionar `tests/OddOddities.UnitTests/Application/Steps/*` e `Application/Pipeline/PipelineOrchestratorTests.cs` com NSubstitute (infraestrutura já preparada no passo 5 das tarefas).
- Mover chave AES, `META_ACCESS_TOKEN` etc. para `dotnet user-secrets` em dev e para o cofre real em prod (já documentado, mas o `appsettings.json` ainda traz valores de exemplo).
