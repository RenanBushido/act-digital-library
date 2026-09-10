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

- O **porquê** de cada decisão vai estar em `design.md` da change, não em comentário de código.
- Todo cenário `GIVEN/WHEN/THEN` da spec vira um teste. Cenário sem teste é change incompleta.
- Não implemente nada fora do escopo declarado em `tasks.md`. Refatoração oportunista pertence a outra change.
- Antes de propor: leia `openspec/specs/` para não contradizer o que já está fixado.
- Toda change passa por duas revisões com a skill `sdd-review`: a especificação antes do `apply`, o código antes do `archive`.

## Estrutura

Projeto único, sem projetos separados por camada e sem regras de dependência entre elas. Pastas: `Domain/` para entidades e value objects compartilhados, `Features/` `src/Library.Api/Features/{Books,Loans,Users,Audit}` para casos de uso, `Infrastructure/` `src/Library.Api/Infrastructure` para persistência, `Common/` para o que atravessa features. O foco neste projeto é transacional, não arquitetural.
Na pasta `src/Library.Api/Extensions` `Extensions` reúne os métodos de registro de serviço (`IServiceCollection`) que configuram a API — um arquivo por área de configuração (ex.: `DatabaseExtensions.cs`, `CachingExtensions.cs`, `ObservabilityExtensions.cs`). Objetivo: manter o `Program.cs` como uma lista curta de chamadas (`builder.Services.AddApiDatabase(...)`, `AddApiCaching(...)`) à medida que mais serviços são adicionados, em vez de crescer indefinidamente. `Infrastructure` continua reservada para os componentes de runtime em si (`AppDbContext` e as `IEntityTypeConfiguration`, em `Infrastructure/Persistence/`); `Extensions` é só a cola de composição/DI sobre eles. `DomainException` mora em `Common/`, junto com `Result`/catálogo de erros — é o que "atravessa features", não um componente de runtime específico.

## Modelo de disponibilidade

`books.available_copies` é um **contador**, não é uma projeção e nem uma tabela de exemplares. Constraint no banco: `CHECK (available_copies >= 0 AND available_copies <= total_copies)`.
A constraint é a garantia independente da lógica — nunca a remova para "simplificar".
`DELETE /books/{id}` desativa (`is_active = false`) e nunca apaga. Livro inativo não aceita
novos empréstimos, mas mantém histórico e aceita devolução dos exemplares já emprestados.

## Concorrência

Fonte da verdade é o PostgreSQL. Isolamento `READ COMMITTED` (padrão) — a atomicidade vem da
instrução, não do nível de isolamento.

Empréstimo = uma transação, três escritas:

1. `ExecuteUpdateAsync` condicional: `Where(b => b.Id == id && b.IsActive && b.AvailableCopies > 0)` decrementando o contador. `affected == 0` => rejeição de negócio, não exceção.
2. Insert do `Loan`.
3. Insert do `AuditEvent`.

Devolução e cancelamento seguem o mesmo desenho, incrementando o contador.

## Idempotência

`POST /loans` exige header `Idempotency-Key`; ausente => 400.
Tabela `idempotency_keys`, PK `(key, endpoint)`, com `request_hash`, `state`, `status_code`,
`response_body`, `expires_at_utc`. Reserve a chave com `INSERT … ON CONFLICT DO NOTHING` **antes** de processar. Gravar a resposta na **mesma transação** do empréstimo.
Repetição bem-sucedida devolve a resposta original com header `Idempotency-Replayed: true` — nunca 409.
Implementado como `IEndpointFilter`, aplicado somente ao endpoint `POST /loans` — não como middleware global.

## Auditoria

Tabela `audit_events`:
`entity_type`,
`entity_id`,
`action`,
`actor`,
`occurred_at_utc`,
`correlation_id`,
`payload` (jsonb com antes/depois dos campos relevantes — não a entidade inteira).
Evento explícito, na **mesma** `SaveChangesAsync` do fato. Nunca via `ILogger`.
Ações mínimas: `BookCreated`, `BookUpdated`, `BookDeactivated`, `LoanCreated`, `LoanReturned`, `LoanCancelled`. O ator vem do header `X-Actor` e na ausência `"anonymous"`.

## Cache

`IDistributedCache` sobre Redis, apenas em leitura pública: `GET /books` e `GET /books/{id}/availability`.
Invalidação com `RemoveAsync` **depois** do `CommitAsync`, nunca antes.
Sempre com TTL como rede de segurança. Falha do Redis é `LogWarning` e a requisição continua: cache indisponível degrada desempenho, jamais correção.

## Contrato HTTP

Minimal APIs com `MapGroup` e uma classe de extensão por feature. `Result<T>` para regra de negócio; exceção só para o inesperado.
Toda resposta de erro é `application/problem+json` com extensões `correlationId` e `traceId`.

| Situação | Status | `type` |
| ------------------- | ----------- | ----------------------- |
| Corpo inválido | 400 | `validation-failed` |
| `Idempotency-Key` ausente | 400 | `idempotency-key-required` |
| Livro/usuário/empréstimo inexistente | 404 | `book-not-found`, `user-not-found`, `loan-not-found` |
| Sem exemplar disponível | 409 | `no-copy-available` |
| Desativação de livro com empréstimo ativo | 409 | `book-has-active-loans` |
| Livro inativo | 409 | `book-inactive` |
| Empréstimo já devolvido/cancelado | 409 | `loan-not-active` |
| Exclusão de livro com histórico | 409 | `book-has-history` |
| Requisição idempotente em voo | 409 | `request-in-flight` |
| Redução de exemplares maior que o disponível | 409 | `insufficient-available-copies` |
| ISBN já cadastrado (após normalização) | 409 | `book-isbn-duplicate` |
| Mesma chave, corpo diferente | 422 | `idempotency-key-reuse` |

Validação: `AddValidation()` nativo na borda (formato, obrigatoriedade, faixas);
Invariantes no construtor da entidade (não emprestar livro inativo, não devolver cancelado).

## Persistência

Todas as colunas que são do tipo DateTime serão `timestamptz`, sempre UTC.
As Migrations deverão serem criadas na pasta `src/Library.Api/Migrations`.
Executar o `MigrateAsync()` no startup **somente** quando `ASPNETCORE_ENVIRONMENT=Development`.
Em Docker e Kubernetes, quem irá migrar é o serviço `migrator` — mesma imagem, com o argumento `--migrate-only`.

## Observabilidade

Middleware de correlação: aceita `X-Correlation-Id` do cliente ou gera um. Entra no escopo de log e volta no header de resposta e é gravado em `audit_events.correlation_id` e no **Problem Details**.

Criar o `Meter` `Library.Loans`, com exatamente estes nomes:

- `library.loans.created` (Counter)
- `library.loans.rejected` (Counter, tag `reason`: `unavailable` | `book_inactive` | `book_not_found`)
- `library.loans.idempotent_replays` (Counter)
- `library.loans.create.duration` (Histogram, ms)

Traces e métricas via OpenTelemetry com exportador OTLP.

Health: `/health/live` **não** consultar Postgres e nem Redis (`Predicate = _ => false`).
`/health/ready` consultar os dois: Postgres como `Unhealthy`, Redis como `Degraded`.

## Testes

Unitários: transições de estado do empréstimo, sem banco.
Integração: Testcontainers com Postgres e Redis reais, `WebApplicationFactory`, container compartilhado por `ICollectionFixture`, limpeza entre testes.
Teste do último exemplar: `Barrier` com **N = 20** requisições, livro com 1 exemplar, **uma `Idempotency-Key` distinta por requisição**, `HttpClient` por tarefa.
Asserções: exatamente um 201, N-1 conflitos, `available_copies == 0`, um único empréstimo ativo.

## Empacotamento

Dockerfile multi-stage; imagem final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, `USER $APP_UID`.
Manifests K8s com `Deployment`, `Service`, probes apontando para `/health/live` e `/health/ready`,`requests`/`limits` de CPU e memória, config e segredos por referência sem valores reais.

## Convenções de código

- Código, identificadores e commits em inglês.
- Documentação e mensagens de commit em português.
- `TimeProvider` injetado. **Nunca** `DateTime.UtcNow` ou `DateTime.Now` direto.
- Todo método de I/O é `async` e recebe `CancellationToken`, propagado do endpoint até a última chamada de EF Core e Redis.
- `TreatWarningsAsErrors` está ligado: warning quebra o build.
- Nullable habilitado; sem `!` para silenciar o compilador.
- Configuração por `appsettings.*.json` e variáveis de ambiente. Nenhum segredo no repositório.

## Proibições

- `rowversion` / `byte[] RowVersion` — é SQL Server, e o banco adotado é PostgreSQL. A estratégia de concorrência deste projeto está fixada na seção **Concorrência**: `UPDATE` condicional atômico.
- `lock`, `SemaphoreSlim`, dicionário estático ou `IMemoryCache` para exclusão mútua ou idempotência.
- Lock distribuído no Redis para decidir empréstimo.
- Ler cache no caminho de decisão de empréstimo.
- `DELETE` físico de livro, empréstimo ou evento de auditoria.
- Provider InMemory do EF Core em testes — não modela locks.
- `MediatR`, `AutoMapper` ou qualquer dependência não listada, sem justificar em
`design.md`.

## Organização do código

- `Domain/<Entidade>/` — entidades e value objects. Invariantes no construtor ou em factory.
- `Features/<Feature>/<CasoDeUso>.cs` — um arquivo por caso de uso, contendo request, validação e handler. Handler é `static`, recebe dependências por parâmetro.
- `Features/<Feature>/<Feature>Endpoints.cs` — apenas roteamento com `MapGroup`. Sem lógica.
- `Features/<Feature>/Contracts/` — DTOs de resposta, com factory `From(entidade)`.
- `Common/` — o que atravessa features: Result, catálogo de erros, paginação, correlação.
- `Infrastructure/Persistence/` — `AppDbContext` e `IEntityTypeConfiguration` por entidade.

Não crie interface com implementação única. Abstração só onde há substituição real
(`TimeProvider`, `IDistributedCache`). Sem `IRepository`, sem `IUnitOfWork`, sem `IService`:
`DbSet` já é repositório e `DbContext` já é unidade de trabalho.
