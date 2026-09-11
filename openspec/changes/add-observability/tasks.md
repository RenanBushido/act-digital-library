## 1. Dependências e infraestrutura de teste

- [x] 1.1 Adicionar ao `Directory.Packages.props` as versões de `AspNetCore.HealthChecks.NpgSql`, `AspNetCore.HealthChecks.Redis`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `Npgsql.OpenTelemetry` e `OpenTelemetry.Instrumentation.StackExchangeRedis`; referenciar (sem versão) em `src/Library.Api/Library.Api.csproj`; verificar que `dotnet restore` conclui sem conflito de versão.
- [x] 1.2 Adicionar `StopPostgresAsync()` a `tests/Library.IntegrationTests/Infrastructure/ApiFixture.cs`, espelhando `StopRedisAsync()`; verificar compilação dos testes.

## 2. Health checks

- [x] 2.1 Registrar `AddHealthChecks()` com o check Postgres (`AddNpgSql`, tag `ready`, `failureStatus: HealthStatus.Unhealthy`) e o check Redis (`AddRedis` usando o `IConnectionMultiplexer` já registrado por `AddApiCaching`, tag `ready`, `failureStatus: HealthStatus.Degraded`) dentro de `Extensions/ObservabilityExtensions.cs` (`AddApiObservability`).
- [x] 2.2 Mapear `GET /health/live` em `Program.cs` com `Predicate = _ => false` e `GET /health/ready` filtrando pela tag `ready`, cada um com um `ResponseWriter` em JSON que lista `status` geral e o `status` de cada check por nome; mapear `HealthStatus.Degraded` para 200 e `HealthStatus.Unhealthy` para 503 nas `HealthCheckOptions` de `/health/ready`.
- [x] 2.3 Teste de integração: com Postgres e Redis no ar, `GET /health/live` e `GET /health/ready` respondem 200 (`tests/Library.IntegrationTests/Infrastructure/HealthChecksTests.cs`).
- [x] 2.4 Teste de integração (spec `observability`, requirement "Liveness health check"): com Postgres parado (`StopPostgresAsync`), `GET /health/live` continua respondendo 200.
- [x] 2.5 Teste de integração (spec `observability`, requirement "Liveness health check"): com Postgres e Redis parados, `GET /health/live` continua respondendo 200.
- [x] 2.6 Teste de integração (spec `observability`, requirement "Readiness health check", cenário "Readiness com PostgreSQL indisponível"): com Postgres parado, `GET /health/ready` responde 503 e o corpo identifica o Postgres como não saudável.
- [x] 2.7 Teste de integração (spec `observability`, requirement "Readiness health check", cenário "Readiness com apenas o Redis indisponível"): com Postgres saudável e Redis parado, `GET /health/ready` responde 200 e o corpo identifica o Redis como degradado e o Postgres como saudável.

## 3. Métricas de empréstimo

- [x] 3.1 Criar o `Meter` `Library.Loans` (`Features/Loans/LoanMetrics.cs`) com o contador `library.loans.created`, o contador `library.loans.rejected` (tag `reason`), o contador `library.loans.idempotent_replays` e o histograma `library.loans.create.duration` em milissegundos (tag `outcome`); registrar via `AddMeter("Library.Loans")` em `Extensions/ObservabilityExtensions.cs`.
- [x] 3.2 Incrementar `library.loans.created` em `Features/Loans/CreateLoan.cs` no caminho de sucesso (depois do commit da transação).
- [x] 3.3 Incrementar `library.loans.rejected` com a tag `reason` correta (`unavailable`, `book_inactive`, `book_not_found`, `user_not_found`) em cada retorno de erro de negócio de `Features/Loans/CreateLoan.cs`.
- [x] 3.4 Incrementar `library.loans.idempotent_replays` em `Features/Loans/RequireIdempotencyKeyFilter.cs`, apenas no caminho de replay (`HandleExistingKeyAsync` retornando o corpo armazenado) - nunca no caminho de negócio do handler nem no caminho de `Idempotency-Key` ausente, `request-in-flight` ou `idempotency-key-reuse`.
- [x] 3.5 Medir `library.loans.create.duration` com um `Stopwatch` iniciado no início de `RequireIdempotencyKeyFilter.InvokeAsync` (antes da reserva da chave) e registrado ao final, com a tag `outcome` derivada do resultado: `created` (201), `replayed` (replay bem-sucedido) ou `rejected` (toda rejeição de negócio E as respostas de idempotência sem reason de negócio - `Idempotency-Key` ausente, `request-in-flight`, `idempotency-key-reuse`); essas três últimas SHALL NOT incrementar `library.loans.rejected` nem `library.loans.idempotent_replays`, só a tag `outcome=rejected` do histograma.
- [x] 3.6 Teste de integração (spec `observability`, requirement "Métricas de empréstimo", cenário "Empréstimo criado com sucesso"): `POST /loans` bem-sucedido incrementa `library.loans.created` em 1 e registra uma observação de `library.loans.create.duration` com `outcome=created` (`tests/Library.IntegrationTests/Features/Loans/LoanMetricsTests.cs`, usando um `MeterListener` ou exportador in-memory para capturar as medições).
- [x] 3.7 Teste de integração (cenário "Rejeição por indisponibilidade de exemplar"): `POST /loans` para um livro sem exemplar disponível incrementa `library.loans.rejected` em 1 com `reason=unavailable` e registra `library.loans.create.duration` com `outcome=rejected`.
- [x] 3.8 Teste de integração (cenário "Rejeição por livro inativo, inexistente ou usuário inexistente"): `POST /loans` para um livro inativo, um livro inexistente e um usuário inexistente incrementa `library.loans.rejected` em 1 em cada caso, com `reason` igual a `book_inactive`, `book_not_found` e `user_not_found`, respectivamente.
- [x] 3.9 Teste de integração (cenário "Repetição idempotente não conta como rejeição"): repetir `POST /loans` com a mesma `Idempotency-Key` e o mesmo corpo de uma requisição já concluída incrementa `library.loans.idempotent_replays` em 1, não incrementa `library.loans.rejected`, e registra `library.loans.create.duration` com `outcome=replayed`.
- [x] 3.10 Teste de integração (cenário "Resposta de idempotência sem reason de negócio não conta como rejected nem replayed"): `POST /loans` sem o header `Idempotency-Key`, com uma chave já `InFlight`, e com a mesma chave e corpo diferente de uma requisição anterior, não incrementam `library.loans.rejected` nem `library.loans.idempotent_replays` em nenhum dos três casos, e cada um registra `library.loans.create.duration` com `outcome=rejected`.

## 4. Logs estruturados

- [x] 4.1 Configurar `builder.Logging.AddJsonConsole(...)` em `Program.cs`.
- [x] 4.2 Adicionar `ILogger` estruturado (identificadores como argumentos, nunca interpolados) para criação em `Features/Loans/CreateLoan.cs` (`LoanId`, `BookId`, `UserId`, `CorrelationId`, `AvailableAfter`), devolução em `Features/Loans/ReturnLoan.cs` e cancelamento em `Features/Loans/CancelLoan.cs`.
- [x] 4.3 Teste de unidade ou integração (spec `observability`, requirement "Logs estruturados de empréstimo"): capturar a saída de log de uma criação de empréstimo bem-sucedida via `ILoggerFactory`/`ITestOutputHelper` de teste e verificar que `LoanId`, `BookId`, `UserId` e `CorrelationId` chegam como propriedades estruturadas do evento de log, não concatenados na mensagem formatada.

## 5. Traces

- [x] 5.1 Registrar `AddOpenTelemetry().WithTracing(...)` em `Extensions/ObservabilityExtensions.cs`: `AddAspNetCoreInstrumentation` (com `Filter` excluindo `/health/live` e `/health/ready`), `AddHttpClientInstrumentation`, a instrumentação Npgsql, a instrumentação StackExchange.Redis (compartilhando o `IConnectionMultiplexer` de `AddApiCaching`), e `AddOtlpExporter` lendo o endpoint de `OTEL_EXPORTER_OTLP_ENDPOINT`.
- [x] 5.2 Registrar `AddOpenTelemetry().WithMetrics(...)` no mesmo método, adicionando `AddMeter("Library.Loans")`, a instrumentação de runtime e `AddOtlpExporter`.
- [x] 5.3 Teste de integração (spec `observability`, requirement "Traces distribuídos", cenário "Endpoints de health não geram trace"): configurar um exportador in-memory de teste, chamar `GET /health/live` e `GET /health/ready`, e verificar que nenhum `Activity` é exportado para essas requisições.
- [x] 5.4 Teste de integração (cenário "Requisição de empréstimo gera trace correlacionado"): com o mesmo exportador in-memory, `POST /loans` produz um `Activity` de ASP.NET Core com spans filhos de Npgsql sob o mesmo `TraceId`.

## 6. Composição e regressão

- [x] 6.1 Criar `Extensions/ObservabilityExtensions.cs` reunindo os registros das seções 2, 3 e 5 em `AddApiObservability(this IServiceCollection services, IConfiguration configuration)`; chamar a partir de `Program.cs`, mantendo-o como lista curta de chamadas.
- [x] 6.2 Teste de integração de regressão (spec `audit-trail`, requirement "Correlation id propagates across the entire API" - já fixado, não modificado nesta change): com a pilha de observabilidade completa registrada, uma resposta de erro de `POST /loans` (por exemplo, livro inexistente) continua carregando `correlationId` no Problem Details, igual ao `X-Correlation-Id` enviado pelo cliente.
- [x] 6.3 Rodar a suíte completa (`dotnet test`) e confirmar que nenhum teste existente regride com `TreatWarningsAsErrors` ligado.
