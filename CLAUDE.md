# Desafio Act-Digital Library

Catálogo e empréstimos de uma biblioteca. Desafio técnico em .NET 10.
Executa em 2 a 11 réplicas: **nenhuma garantia de correção pode depender de estado em processo.**

## Stack

- .NET 10 LTS
- ASP.NET Core Minimal APIs
- EF Core 10 + Npgsql
- PostgreSQL
- Redis
- xUnit + Testcontainers
- Docker + Docker Compose
- SDD (Spec Driven Development) + Claude Code
- OpenTelemetry

## Processo

Spec Driven Development com OpenSpec. Uma change por capacidade.

- O **porquê** de cada decisão vai em `design.md` da change, não em comentário de código.
- Todo cenário `GIVEN/WHEN/THEN` da spec vira um teste. Cenário sem teste é change incompleta.
- Não implemente nada fora do escopo declarado em `tasks.md`. Refatoração oportunista pertence a outra change.
- Antes de propor: leia `openspec/specs/` para não contradizer o que já está fixado.
- Toda change passa por duas revisões com a skill `sdd-review`: a especificação antes do `apply`, o código antes do `archive`.
- Change que altera comportamento já especificado usa delta `MODIFIED`, não apenas `ADDED`.

## Organização do código

Projeto único, sem projetos separados por camada e sem regras de dependência entre elas.
O foco deste projeto é transacional, não arquitetural.

- `Domain/<Entidade>/` — entidades e value objects compartilhados. Invariantes no construtor ou em factory.
- `Features/<Feature>/<CasoDeUso>.cs` — um arquivo por caso de uso: request, validação e handler. Handler é `static` e recebe dependências por parâmetro.
- `Features/<Feature>/<Feature>Endpoints.cs` — só roteamento com `MapGroup`. Sem lógica.
- `Features/<Feature>/Contracts/` — DTOs de resposta, com factory `From(entidade)`.
- `Common/` — o que atravessa features: `Result`, catálogo de erros, `DomainException`, paginação, correlação.
- `Infrastructure/Persistence/` — `AppDbContext` e `IEntityTypeConfiguration` por entidade.
- `Infrastructure/Caching/` — `BookCache`.
- `Extensions/` — só a cola de composição e DI: um arquivo por área (`DatabaseExtensions.cs`, `CachingExtensions.cs`, `ObservabilityExtensions.cs`). O objetivo é manter o `Program.cs` como uma lista curta de chamadas.

Duas coisas diferentes usam a palavra "extensão", não confunda: `<Feature>Endpoints.cs` estende `IEndpointRouteBuilder` e mapeia rotas; os arquivos de `Extensions/` estendem
`IServiceCollection` e registram serviços.

Não crie interface com implementação única. Abstração só onde há substituição real (`TimeProvider`, `IDistributedCache`). Sem `IRepository`, sem `IUnitOfWork`, sem `IService`:
`DbSet` já é repositório e `DbContext` já é unidade de trabalho.

## Modelo de disponibilidade

`books.available_copies` é um **contador**, não é projeção nem tabela de exemplares.
Constraint no banco: `CHECK (available_copies >= 0 AND available_copies <= total_copies)`.
A constraint é a garantia independente da lógica — nunca a remova para "simplificar".

`DELETE /books/{id}` desativa (`is_active = false`) e nunca apaga. Livro inativo não aceita
novos empréstimos, mas mantém histórico e aceita devolução dos exemplares já emprestados.
Livro com empréstimo ativo não pode ser desativado.

## Concorrência

Fonte da verdade é o PostgreSQL. Isolamento `READ COMMITTED` (padrão) — a atomicidade vem da instrução, não do nível de isolamento.

Empréstimo = uma transação, três escritas:

1. `ExecuteUpdateAsync` condicional: `Where(b => b.Id == id && b.IsActive && b.AvailableCopies > 0)` decrementando o contador. `affected == 0` => rejeição de negócio, não exceção.
2. Insert do `Loan`.
3. Insert do `AuditEvent`.

Devolução e cancelamento seguem o mesmo desenho, incrementando o contador.

## Idempotência

`POST /loans` **exige** o header `Idempotency-Key`; ausente => 400. O enunciado pede apenas que o endpoint aceite o header — torná-lo obrigatório é restrição deliberada do contrato, registrada no design: operação que altera estoque não deve poder ser repetida sem chave.

Tabela `idempotency_keys`, PK `(key, endpoint)`, com `request_hash`, `state`(`InFlight` | `Completed`), `status_code`, `response_body`, `resource_id`, `created_at_utc`
e `expires_at_utc` (janela de 24 h).

`request_hash` = SHA-256 dos **bytes crus** do corpo da requisição mais o template da rota.
Exige `Request.EnableBuffering()` antes da leitura. Nunca reserializar o DTO para hashear.

### Divisão de responsabilidade

Não tente gravar a resposta no filtro: o `IResult` só é serializado depois que o handler retorna, quando a transação já commitou.

- **Filtro** (`IEndpointFilter`, só em `POST /loans`, nunca middleware global): valida presença do header, calcula o hash, reserva a chave com `INSERT … ON CONFLICT DO NOTHING`. Se a chave já existia: hash diferente → 422; `Completed` → devolve `status_code` e `response_body` armazenados com header `Idempotency-Replayed: true`; `InFlight` → 409 `request-in-flight`. Libera a chave se o handler falhar.
- **Handler**: dentro da transação do empréstimo, serializa o DTO de resposta e grava `status_code`, `response_body` e `resource_id`, marcando a linha como `Completed`.

Apenas respostas 2xx são armazenadas. Falha de negócio ou exceção libera a chave, para que uma nova tentativa legítima seja reavaliada — rejeição por indisponibilidade é dependente do tempo e não deve virar resultado permanente.

Replay de requisição concluída devolve a resposta original, nunca 409. Os 409 (`request-in-flight`) e o 422 (`idempotency-key-reuse`) da tabela de erros são situações distintas de replay.

## Auditoria

Tabela `audit_events`, append-only: `id` (bigint identity), `entity_type`, `entity_id`, `action`, `actor`, `occurred_at_utc` (timestamptz), `correlation_id`, `payload` (jsonb).
Índices: `(entity_type, entity_id, occurred_at_utc)` e `(correlation_id)`.

Evento explícito, gravado na **mesma** `SaveChangesAsync` ou transação do fato que o originou.
Nunca via `ILogger`. Não usar interceptor de `SaveChanges`: ele não observa `ExecuteUpdateAsync`, que é o caminho usado nas alterações de quantidade.

Ações: `BookCreated`, `BookUpdated`, `BookDeactivated`, `LoanCreated`, `LoanReturned`, `LoanCancelled`.

Formato do `payload`, padronizado para ser consultável — apenas os campos que mudaram, nunca a entidade inteira:

- criação → `{ "after": { ... } }`
- alteração → `{ "before": { ... }, "after": { ... } }`
- desativação → `{ "before": { "isActive": true }, "after": { "isActive": false } }`

Para obter o estado anterior em operações que usam `ExecuteUpdate`, aplique o UPDATE com `RETURNING` e derive o anterior do delta conhecido. Não fazer `SELECT` antes do UPDATE.

A entidade não expõe setters públicos e nenhum endpoint altera ou remove eventos. Em produção, o papel da aplicação não teria `UPDATE`/`DELETE` nessa tabela — declarado como evolução.

`actor` vem do header `X-Actor`; ausente, `"anonymous"`. Sem autenticação no escopo.
`correlation_id` vem do middleware descrito em **Observabilidade**.

## Cache

Redis via `IDistributedCache`, **apenas em leitura pública**. Nunca no caminho de decisão de empréstimo: disponibilidade para decidir vem sempre do PostgreSQL.

Não usar `InstanceName` no `AddStackExchangeRedisCache`: ele prefixa as chaves do `IDistributedCache` mas não as do `IConnectionMultiplexer`, criando duas convenções.
O prefixo `library:` é escrito explicitamente dentro de `BookCache`.
O `IConnectionMultiplexer` precisa ser registrado à parte e compartilhado com o cache via `ConnectionMultiplexerFactory`.

Chaves e TTL (configuráveis em `Cache:*`):

- `book:{id}:availability` — TTL 60 s. Chave direta, invalidada por `RemoveAsync`.
- `books:list:v{versão}:{hash dos filtros}` — TTL 120 s. A versão vive em `books:list:version` no Redis. Invalidar = `INCR` nessa versão; as chaves antigas ficam órfãs e expiram sozinhas.
  Nunca usar `KEYS` ou `SCAN` para apagar por padrão.

O hash dos filtros cobre `page`, `pageSize`, `title`, `author`, `isbn` e `includeInactive`, normalizados e em ordem fixa.

Serialização com `System.Text.Json`, sem configuração especial.

Toda leitura e escrita de cache passa pela classe concreta `BookCache` — sem interface, sem mock.
Ela centraliza nomes de chave, TTL e tratamento de falha. Nos testes, o Redis é real (Testcontainers) e a asserção é feita direto nele.

Invalidação **depois** do commit — ou, em operação sem transação explícita, depois de a instrução retornar. Nunca antes: invalidar antes permite que outra réplica releia o valor não commitado e repopule o cache com dado velho.

Redis indisponível degrada desempenho, nunca correção:

- falha na leitura → `LogWarning` e busca no banco
- falha na invalidação → `LogWarning` e a requisição segue; o TTL é a rede de segurança

Não há proteção contra cache stampede: N réplicas podem recalcular o mesmo item ao expirar.
Limitação declarada no README; `HybridCache` resolveria, ao custo do L1 local desatualizado.

## Contrato HTTP

Minimal APIs com `MapGroup`. `Result<T>` para regra de negócio; exceção só para o inesperado.
Toda resposta de erro é `application/problem+json` com extensões `correlationId` e `traceId`.

| Situação | Status | `type` |
| --- | --- | --- |
| Corpo inválido | 400 | `validation-failed` |
| `Idempotency-Key` ausente | 400 | `idempotency-key-required` |
| Livro/usuário/empréstimo inexistente | 404 | `book-not-found`, `user-not-found`, `loan-not-found` |
| Sem exemplar disponível | 409 | `no-copy-available` |
| Desativação de livro com empréstimo ativo | 409 | `book-has-active-loans` |
| Livro inativo | 409 | `book-inactive` |
| Empréstimo já devolvido/cancelado | 409 | `loan-not-active` |
| Requisição idempotente em voo | 409 | `request-in-flight` |
| Redução de exemplares maior que o disponível | 409 | `insufficient-available-copies` |
| ISBN já cadastrado (após normalização) | 409 | `book-isbn-duplicate` |
| Mesma chave, corpo diferente | 422 | `idempotency-key-reuse` |

Validação: `AddValidation()` nativo na borda (formato, obrigatoriedade, faixas).
Invariantes no construtor da entidade (não emprestar livro inativo, não devolver cancelado).

## Persistência

Todas as colunas de data e hora são `timestamptz`, sempre UTC.
As migrations ficam em `src/Library.Api/Migrations`.
`MigrateAsync()` no startup **somente** quando `ASPNETCORE_ENVIRONMENT=Development`.
Em Docker e Kubernetes quem migra é o serviço `migrator` — mesma imagem, argumento `--migrate-only`.

## Observabilidade

A correlação é entregue pelo middleware criado na change `add-domain-audit`: aceita `X-Correlation-Id` do cliente ou gera um; o mesmo identificador entra no escopo de log, volta no header da resposta, alimenta o Problem Details e é gravado em `audit_events.correlation_id`.

### Logs

`ILogger` nativo, sem Serilog. Saída em JSON no console (`AddJsonConsole`), para que o
coletor do orquestrador consuma sem parser próprio.

Operações relevantes logam com identificadores de negócio como propriedades estruturadas,nunca interpoladas na mensagem: `LoanId`, `BookId`, `UserId`, `CorrelationId` e, quando
houver, `AvailableAfter`.
Log de negócio não substitui auditoria; a trilha é a tabela.

### Metricas

`Meter` `Library.Loans`, com exatamente estes nomes:

- `library.loans.created` (Counter)
- `library.loans.rejected` (Counter, tag `reason`: `unavailable` | `book_inactive` |
  `book_not_found` | `user_not_found`). Réplica idempotente **não** é rejeição e não incrementa este contador.
- `library.loans.idempotent_replays` (Counter)
- `library.loans.create.duration` (Histogram, ms, tag `outcome`: `created` | `replayed` |
  `rejected`). Mede o **endpoint inteiro**, incluindo o filtro de idempotência. A tag existe
porque criação e replay têm ordens de grandeza diferentes e misturá-las torna o percentil sem significado.

A métrica nativa `http.server.request.duration` continua ativa. A métrica própria não a
substitui: existe para permitir o corte por `outcome`.

### Traces

OpenTelemetry com exportador OTLP. Instrumentar: ASP.NET Core, HttpClient, Npgsql,StackExchange.Redis e runtime. Excluir `/health/live` e `/health/ready` do tracing —
com N réplicas e probes periódicos, eles dominam o volume sem informação útil.

Endpoint OTLP por configuração (`OTEL_EXPORTER_OTLP_ENDPOINT`), nunca fixo no código.

### Health checks

- `/health/live` — não consulta Postgres nem Redis (`Predicate = _ => false`). Confirma apenas que o processo responde.
- `/health/ready` — consulta as dependências marcadas com a tag `ready`:
  - Postgres com `failureStatus: Unhealthy` → resposta **503**, o pod sai do balanceador.
  - Redis com `failureStatus: Degraded` → resposta **200**, o pod continua servindo.

`Degraded` devolvendo 200 é deliberado e não deve ser "corrigido": cache indisponível é degradação de desempenho, não indisponibilidade. Mapear `Degraded` para 503 tiraria todas
as réplicas do balanceador ao mesmo tempo, transformando uma falha de cache em queda total.

## Testes

Unitários: transições de estado do empréstimo e value objects, sem banco.
Integração: Testcontainers com Postgres e Redis reais, `WebApplicationFactory`, container compartilhado por `ICollectionFixture`, limpeza entre testes.

Três testes que provam os requisitos centrais do desafio:

- **Último exemplar**: `Barrier` com N = 20 requisições, livro com 1 exemplar, uma `Idempotency-Key` distinta por requisição, `HttpClient` por tarefa. Exatamente um 201, N-1 conflitos, `available_copies == 0`, um único empréstimo ativo.
- **Idempotência concorrente**: N requisições simultâneas com a **mesma** chave e o mesmo corpo produzem exatamente um empréstimo; as demais recebem replay ou `request-in-flight`.
- **Cache envenenado**: com uma disponibilidade falsa e maior gravada à mão no Redis, a tentativa de empréstimo continua sendo rejeitada pelo banco. É o que prova que o Postgres é a fonte de verdade.

## Empacotamento

Dockerfile multi-stage; imagem final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`,`USER $APP_UID`.
Manifests K8s com `Deployment`, `Service`, probes em `/health/live` e `/health/ready`, `requests`/`limits` de CPU e memória, config e segredos por referência sem valores reais.

## Convenções de código

- Código e identificadores em inglês.
- Documentação e mensagens de commit em português, com prefixo Conventional Commits (`feat`, `fix`, `chore`, `docs`, `test`, `refactor`) em inglês.
- `TimeProvider` injetado. **Nunca** `DateTime.UtcNow` ou `DateTime.Now` direto.
- Todo método de I/O é `async` e recebe `CancellationToken`, propagado do endpoint até a última chamada de EF Core e Redis.
- `TreatWarningsAsErrors` está ligado: warning quebra o build.
- Nullable habilitado; sem `!` para silenciar o compilador.
- Configuração por `appsettings.*.json` e variáveis de ambiente. Nenhum segredo no repositório.

## Proibições

- `rowversion` / `byte[] RowVersion` — é SQL Server, e o banco adotado é PostgreSQL. A estratégia deste projeto está fixada na seção **Concorrência**: `UPDATE` condicional atômico.
- `lock`, `SemaphoreSlim`, dicionário estático ou `IMemoryCache` para exclusão mútua ou idempotência.
- Lock distribuído no Redis para decidir empréstimo.
- Ler cache no caminho de decisão de empréstimo.
- `KEYS` ou `SCAN` para invalidar cache por padrão de chave.
- `DELETE` físico de livro, empréstimo ou evento de auditoria.
- Provider InMemory do EF Core em testes — não modela locks.
- Interface com implementação única.
- `MediatR`, `AutoMapper` ou qualquer dependência não listada, sem justificar em `design.md`.
