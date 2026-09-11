## Context

Ver `proposal.md` - Why. Estado atual relevante para o desenho:

- `CorrelationIdMiddleware` (de `add-domain-audit`) já popula `httpContext.Items[CorrelationIdMiddleware.ItemKey]`, o header de resposta e o Problem Details (`ProblemDetailsExtensions.cs`). Esta change consome, não altera.
- `POST /loans` já tem o pipeline completo descrito em CLAUDE.md: `RequireIdempotencyKeyFilter` (reserva a chave, decide replay/409/422) envolvendo `CreateLoan.HandleAsync` (transação com `ExecuteUpdateAsync` condicional, insert do `Loan`, insert do `AuditEvent`, marcação da chave como `Completed`).
- `Extensions/` já tem `DatabaseExtensions.cs` e `CachingExtensions.cs`, cada um só registrando serviços; `Program.cs` é uma lista curta de chamadas. Esta change segue o mesmo padrão com `ObservabilityExtensions.cs`.
- Não há hoje nenhum `Meter`, nenhum health check, nenhum `AddOpenTelemetry`, nenhuma configuração de logging além do console padrão do template.
- `ApiFixture` (testes de integração) já sobe Postgres e Redis reais via Testcontainers e expõe `StopRedisAsync()`; não existe `StopPostgresAsync()` ainda.
- Gerenciamento central de pacotes (`Directory.Packages.props`) - toda versão nova entra lá, nunca inline no `.csproj`.

## Goals / Non-Goals

**Goals:**
- Separar liveness de readiness com a semântica exata do CLAUDE.md (Redis degradado não tira réplica do ar).
- Instrumentar `POST /loans` com as quatro métricas de `Library.Loans`, sem que a métrica de indisponibilidade minta em réplica idempotente (cenário 6 do enunciado).
- Emitir log estruturado em JSON para empréstimo/devolução/cancelamento, com `CorrelationId` correlacionando com o trace e a auditoria.
- Ligar OpenTelemetry (traces + métricas) com exportador OTLP configurável, excluindo os endpoints de health.
- Provar por teste de integração que a regressão do `correlationId` no Problem Details continua coberta com a nova pilha de observabilidade em cima.

**Non-Goals:**
- Não reimplementar ou alterar `CorrelationIdMiddleware`.
- Não criar dashboards, alertas ou regras de scraping - isso é operação de plataforma, fora do escopo desta change.
- Não escolher ou fixar um backend OTLP específico (Jaeger, Tempo, Collector); só a configuração do endpoint.
- Não instrumentar outros endpoints de negócio além dos já cobertos por ASP.NET Core/Npgsql/Redis automaticamente - só `POST /loans` ganha métricas de negócio dedicadas.
- Não adicionar manifests Kubernetes (fora de escopo, declarado no proposal).

## Decisions

### Health checks com `Microsoft.Extensions.Diagnostics.HealthChecks`

`GET /health/live` usa `Predicate = _ => false`, sem checks registrados no cálculo - confirma só que o processo responde. `GET /health/ready` filtra pela tag `ready`, com dois checks:

- Postgres (`AddNpgSql`, tag `ready`, `failureStatus: HealthStatus.Unhealthy`) - indisponível => `Unhealthy` => 503.
- Redis (`AddRedis`, usando o `IConnectionMultiplexer` já registrado em `CachingExtensions`, tag `ready`, `failureStatus: HealthStatus.Degraded`) - indisponível => `Degraded` => mapeado para 200.

**Por que `Degraded` devolve 200 e o efeito no orquestrador:** o mapeamento padrão do health check ASP.NET Core traduz qualquer status diferente de `Healthy` em 503 quando não customizado. Isso forçaria a saída do Redis a comportar-se como indisponibilidade total, contrariando a regra do domínio (cache é leitura pública, nunca decide empréstimo - ver "Cache" no CLAUDE.md). Por isso o `ResultStatusCodes` do `HealthCheckOptions` de `/health/ready` mapeia explicitamente `HealthStatus.Degraded -> 200`. Com N réplicas, se o Redis cair uma vez, todas as réplicas ficariam `Degraded` ao mesmo tempo; se isso virasse 503, o orquestrador tiraria 100% da capacidade do ar por uma dependência que só degrada latência/desempenho, transformando uma falha de cache em indisponibilidade total do serviço. Resposta 200 com corpo indicando o estado degradado mantém o tráfego fluindo (lendo do Postgres) e ainda torna o problema observável.

**Corpo da resposta:** `ResponseWriter` customizado (JSON com `status` geral e um objeto por check, incluindo `status` individual) - não o texto default de uma linha, porque a spec exige identificar qual dependência está em qual estado.

**Conexão usada pelo check do Postgres:** `AddNpgSql` recebe a mesma connection string de `ConnectionStrings:Postgres` (a mesma lida por `AddApiDatabase`), não uma nova fonte de configuração - mas abre sua própria conexão ADO.NET por verificação, fora do pool do `AppDbContext`. Aceitável: readiness é consultado por probe do orquestrador em intervalo de segundos, não por requisição de negócio, então uma conexão extra e de vida curta por poll não compete de forma relevante com o pool da aplicação. Não reusar o `DbContext`/`NpgsqlDataSource` registrado evita acoplar o health check ao ciclo de vida (escopo por requisição) do `DbContext`, que não existe fora de uma requisição HTTP.

**Alternativa descartada - health check único sem separação:** um único `/health` que consulta Postgres e Redis serviria só para monitoramento externo, não para decisão do orquestrador. Sem separar liveness de readiness, um Postgres lento (não caído) faria o orquestrador reiniciar o processo (perdendo liveness) em vez de só parar de rotear tráfego (perdendo readiness) - o problema errado seria "corrigido" com a ação errada (restart em vez de remoção do balanceador).

### Métricas com `System.Diagnostics.Metrics` puro (sem prometheus-net)

Um `Meter` estático `Library.Loans`, registrado via `AddOpenTelemetry().WithMetrics(b => b.AddMeter("Library.Loans"))`, exportado via OTLP.

**Por que existe `library.loans.create.duration` mesmo com `http.server.request.duration` nativo:** a métrica nativa mede qualquer requisição HTTP por rota e verbo, sem saber que `POST /loans` tem dois caminhos de execução com ordens de grandeza de latência diferentes: criação real (transação com três escritas) e replay idempotente (um SELECT e retorno do corpo salvo). Cortar `http.server.request.duration` por rota ainda mistura os dois casos no mesmo histograma, arruinando qualquer percentil. A métrica própria carrega a tag `outcome` (`created`/`replayed`/`rejected`) e mede o endpoint inteiro - incluindo o tempo gasto no `IEndpointFilter` de idempotência - porque é isso que a réplica de fato paga por requisição.

**Onde a duração é medida:** um `Stopwatch` iniciado no início do `RequireIdempotencyKeyFilter.InvokeAsync` (antes da reserva da chave) e finalizado depois que o `IResult` do pipeline inteiro retorna, com o `outcome` derivado do próprio resultado (replay detectado pelo header `Idempotency-Replayed` ou pelo tipo de resultado retornado por `HandleExistingKeyAsync`; rejeição de negócio detectada pelo status code do `Result`/Problem Details; sucesso pelo 201). Medir no filtro (não só no handler) é obrigatório porque o filtro é o único lugar que envolve os três caminhos (replay, in-flight, criação real).

**Mapeamento de `outcome` para os desfechos do filtro que não são negócio.** `library.loans.rejected` é reservado às quatro razões de negócio (`unavailable`, `book_inactive`, `book_not_found`, `user_not_found`) — são elas que um operador usa para julgar pressão sobre o catálogo. `Idempotency-Key` ausente (400), chave em voo (409 `request-in-flight`) e reuso de chave com corpo diferente (422 `idempotency-key-reuse`) não são nem isso nem um replay bem-sucedido: não incrementam `library.loans.rejected` (misturariam causas de negócio com erro de uso do header/contenção transitória) nem `library.loans.idempotent_replays` (não devolveram a resposta de uma requisição concluída). Para a tag `outcome` do histograma — que só tem três valores possíveis — essas três respostas recebem `outcome=rejected`, porque são, do ponto de vista de latência do endpoint, requisições que terminam sem produzir uma criação nem devolver o corpo de uma já concluída; ficam fora do contador `rejected`, mas dentro da tag `rejected` do histograma. Essa distinção contador vs. tag está fixada em `specs/observability/spec.md` (requirement "Métricas de empréstimo") para não ficar a critério de quem implementar.

**Por que réplica idempotente não incrementa `rejected` (cenário 6):** `rejected` existe para medir indisponibilidade real de negócio (`unavailable`, `book_inactive`, `book_not_found`, `user_not_found`) - sinais que um operador usa para decidir se o catálogo está sob pressão. Uma réplica idempotente devolve o mesmo status 2xx da requisição original armazenada; contá-la como rejeição inflaria (ou, pior, mascararia) a taxa real de indisponibilidade toda vez que um cliente retentasse por timeout de rede, fazendo a métrica mentir sobre o estado do sistema. Por isso o incremento de `rejected` só acontece no caminho de negócio dentro de `CreateLoan.HandleAsync`/validações anteriores, nunca no caminho de replay do filtro, e o teste de integração do cenário 6 verifica os dois contadores juntos (replay incrementa um e não incrementa o outro) para essa distinção não regredir silenciosamente.

**Alternativa descartada - `prometheus-net` com endpoint `/metrics` próprio:** exigiria um segundo pipeline de exposição de métricas (scrape HTTP) paralelo ao OTLP já adotado para traces, duas convenções de nome de métrica (Prometheus normaliza nomes com pontos para underscore) e um exportador a mais para manter consistente. Ficando só em `System.Diagnostics.Metrics` + exportador OTLP, o mesmo pipeline de traces serve para métricas, com um único exportador e uma única configuração de endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`).

### Logging: `AddJsonConsole` nativo, sem Serilog

`builder.Logging.AddJsonConsole(...)` substitui (ou complementa) o console padrão. Identificadores de negócio (`LoanId`, `BookId`, `UserId`, `CorrelationId`, `AvailableAfter`) entram como argumentos estruturados do `ILogger` (`LogInformation("Loan {LoanId} created for book {BookId}", loanId, bookId)`), nunca interpolados na mensagem - o `AddJsonConsole` já serializa esses argumentos como campos JSON individuais.

**Alternativa descartada - Serilog:** dependência não listada no CLAUDE.md (`Proibições`); `AddJsonConsole` nativo já produz JSON estruturado consumível por um coletor sem parser próprio, sem exigir sink, enricher ou configuração adicional.

### Tracing: `OpenTelemetry.Extensions.Hosting` + instrumentações por pacote

`AddOpenTelemetry().WithTracing(b => b.AddAspNetCoreInstrumentation(...).AddHttpClientInstrumentation().AddNpgsql().AddRedisInstrumentation(multiplexer).AddOtlpExporter())`.

`AddAspNetCoreInstrumentation` recebe `Filter` excluindo `/health/live` e `/health/ready` (comparação pelo `HttpContext.Request.Path`) - motivo já registrado no CLAUDE.md: com N réplicas e probes periódicos, esses endpoints dominariam o volume de spans sem informação útil.

`AddRedisInstrumentation()` é chamado sem argumento (não `AddRedisInstrumentation(multiplexer)`): o pacote resolve o `IConnectionMultiplexer` do container quando o `TracerProvider` é montado, reaproveitando o mesmo singleton já registrado em `CachingExtensions`. Isso não é só estilo - passar a instância diretamente exigiria resolvê-la no momento do registro (`AddApiObservability` só tem o `IServiceCollection`, não o provider construído); e envolvê-la manualmente numa configuração adiada (`ConfigureServices` dentro de um callback `(sp, builder) => ...`) falha em runtime com `NotSupportedException: Services cannot be configured after ServiceProvider has been created` - `AddRedisInstrumentation` registra serviços auxiliares internamente e só pode fazer isso enquanto o container ainda está sendo montado. A forma sem argumento existe no pacote exatamente para esse cenário (multiplexer registrado no DI da aplicação) e resolve o singleton sozinha no momento certo.

### Composição em `Extensions/ObservabilityExtensions.cs`

Um único método `AddApiObservability(this IServiceCollection services, IConfiguration configuration)` (nomeando o padrão dos outros arquivos de `Extensions/`) registra: `Meter`, `AddOpenTelemetry` (tracing + métricas), `AddHealthChecks` com os dois checks. `builder.Logging.AddJsonConsole` fica em `Program.cs` diretamente (é `ILoggingBuilder`, não `IServiceCollection`, então não cabe na mesma assinatura dos outros métodos de extensão) - mantendo `Program.cs` como lista curta de chamadas, sem lógica.

Os endpoints `MapHealthChecks("/health/live", ...)` e `MapHealthChecks("/health/ready", ...)` são mapeados direto em `Program.cs`, junto dos outros `Map*Endpoints()` - não há caso de uso, validação ou contrato de resposta que justifique um arquivo de feature dedicado (são dois `MapHealthChecks` com opções, não um handler).

## Risks / Trade-offs

- [Instrumentar a duração no filtro de idempotência acopla `RequireIdempotencyKeyFilter` a um `Meter` de `Features/Loans`] → aceitável: o filtro já é específico de `POST /loans` (`Endpoint = "POST /loans"` fixo), não é reutilizado por outro endpoint: acoplá-lo à métrica desse mesmo endpoint não cria uma dependência nova de fato.
- [`AddNpgSql`/`AddRedis` (pacotes `AspNetCore.HealthChecks.*`) são dependências novas não usadas antes] → risco baixo: são pacotes de escrita única (só o predicado de saúde), sem superfície de API que vaze para o resto do código; alternativa de escrever os checks à mão foi descartada por reimplementar lógica de retry/timeout que os pacotes já resolvem corretamente.
- [Sem proteção contra cache stampede nas métricas de leitura do catálogo] → fora do escopo desta change (já declarado como limitação em `add-availability-cache`); não é reintroduzido nem agravado aqui.
- [Exportador OTLP sem coletor configurado em ambiente local] → degrada para: o SDK do OpenTelemetry loga falha de exportação e descarta o lote, sem afetar a resposta HTTP; não há dependência dura de um coletor estar de pé para a aplicação subir.

## Migration Plan

Sem mudança de schema nem de dado. Passos de implantação: build da imagem com os pacotes novos, deploy normal (rolling); nenhuma migration de banco nesta change. Rollback é reverter a imagem - health checks, métricas e logs anteriores (ausentes) voltam ao estado anterior sem efeito colateral em dado persistido.
