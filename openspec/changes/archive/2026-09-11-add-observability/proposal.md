## Why

A aplicação roda em 2 a 11 réplicas atrás de um orquestrador, mas hoje não expõe nada que permita a esse orquestrador decidir se uma réplica deve receber tráfego, nem que permita a um operador enxergar o que está acontecendo dentro do processo: não há separação entre "o processo está vivo" e "o processo está pronto para servir", não há métricas de negócio para os empréstimos, e não há traces correlacionando requisições HTTP com as consultas de banco e cache que elas disparam. Sem isso, uma falha de PostgreSQL ou Redis só é percebida por sintoma (erros 5xx acumulando) em vez de sinalizada de forma que o orquestrador reaja corretamente — e "reagir corretamente" inclui não tirar todas as réplicas do ar por causa só do cache, que é degradação de desempenho, não indisponibilidade.

## What Changes

- Adiciona `GET /health/live`: confirma apenas que o processo responde, sem consultar PostgreSQL ou Redis.
- Adiciona `GET /health/ready`: consulta PostgreSQL (tag `ready`, `failureStatus: Unhealthy` → 503) e Redis (tag `ready`, `failureStatus: Degraded` → 200); o corpo identifica o estado de cada dependência.
- Adiciona o `Meter` `Library.Loans` com os contadores `library.loans.created`, `library.loans.rejected` (tag `reason`), `library.loans.idempotent_replays`, e o histograma `library.loans.create.duration` (tag `outcome`), instrumentando `POST /loans` (handler e filtro de idempotência).
- Adiciona logging estruturado em JSON (`AddJsonConsole`) para as operações de empréstimo, devolução e cancelamento, com identificadores de negócio como propriedades estruturadas.
- Adiciona instrumentação OpenTelemetry (traces e métricas) para ASP.NET Core, HttpClient, Npgsql e StackExchange.Redis, com exportador OTLP configurável por `OTEL_EXPORTER_OTLP_ENDPOINT`, excluindo `/health/live` e `/health/ready` do tracing.
- Adiciona `Extensions/ObservabilityExtensions.cs` para centralizar esse registro, mantendo `Program.cs` como lista curta de chamadas.
- Consome o `CorrelationIdMiddleware` existente (de `add-domain-audit`) sem alterá-lo.

## Capabilities

### New Capabilities
- `observability`: health checks separados por finalidade (liveness/readiness), métricas de negócio do `Meter Library.Loans`, logging estruturado e tracing OpenTelemetry para requisições de empréstimo.

### Modified Capabilities

Nenhuma. O requisito de correlação (`audit-trail`, requirement 4) já cobre a propagação do `correlationId` para o Problem Details; esta change consome esse middleware sem alterar seu contrato. A regressão citada nos critérios de aceitação é validada por um teste de integração desta change, não por uma mudança de requisito.

## Impact

- Código novo: `Extensions/ObservabilityExtensions.cs`, endpoints de health, `Meter`/instrumentos em `Features/Loans`, configuração de logging JSON.
- Código existente tocado: `Program.cs` (registro dos novos serviços e mapeamento dos endpoints de health), `Features/Loans/CreateLoan.cs` e `Features/Loans/RequireIdempotencyKeyFilter.cs` (instrumentação de métricas), `appsettings.*.json` (seção `OTEL_EXPORTER_OTLP_ENDPOINT` e opções de health).
- Dependências novas: pacotes `Microsoft.Extensions.Diagnostics.HealthChecks` (Npgsql e Redis) e OpenTelemetry (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, `.Http`, e o pacote de instrumentação Npgsql/StackExchange.Redis usados pelo ecossistema OpenTelemetry).
- Testes: `tests/Library.IntegrationTests` ganha testes de health (Postgres/Redis parados) e de métricas (criação, rejeição por indisponibilidade, replay idempotente, histograma por `outcome`), além do teste de regressão do `correlationId`.
