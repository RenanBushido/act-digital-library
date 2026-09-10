## 1. Domínio

- [x] 1.1 Implementar `Domain/Loan/LoanStatus.cs` (`Active`, `Returned`, `Cancelled`) e `Domain/Loan/Loan.cs`: factory `Create(bookId, userId, dueDays, timeProvider)`, métodos `Return(timeProvider)`/`Cancel(timeProvider)` que só transicionam a partir de `Active` (lançam `DomainException` caso contrário — usados pelo teste unitário de transição de estado, não pelo caminho HTTP, que decide via `ExecuteUpdateAsync` condicional). Teste unitário (sem banco) cobrindo: criação válida (status `Active`, `DueAtUtc = LoanedAtUtc + dueDays`), `Return`/`Cancel` a partir de `Active` funcionam, `Return`/`Cancel` a partir de `Returned`/`Cancelled` lançam.
- [x] 1.2 Implementar `Domain/Audit/AuditEvent.cs`: factory `Create(entityType, entityId, action, actor, occurredAtUtc, correlationId, payload)`. Teste unitário cobrindo criação válida.

## 2. Persistência

- [x] 2.1 Implementar `Infrastructure/Persistence/LoanConfiguration.cs`: tabela `loans`, `Status` com `HasConversion<string>()`, colunas `timestamptz` para as datas, FK `book_id`→`books.id` e `user_id`→`users.id` (relacionamento sombra, sem propriedade de navegação em `Loan`), índices não únicos em `book_id` e `user_id`, sem índice de unicidade em `(book_id, user_id)`.
- [x] 2.2 Implementar `Infrastructure/Persistence/AuditEventConfiguration.cs`: tabela `audit_events`, `payload` como `jsonb`.
- [x] 2.3 Adicionar `DbSet<Loan>` e `DbSet<AuditEvent>` a `AppDbContext`.
- [x] 2.4 Gerar a migration (`dotnet ef migrations add AddLoanConcurrency`) criando `loans` e `audit_events`; revisar manualmente confirmando `timestamptz` e `jsonb`; verificar com `dotnet ef migrations has-pending-model-changes` que não sobra diff.
- [x] 2.5 Aplicar a migration contra Postgres local (`dotnet ef database update`) e conferir o schema via `psql \d loans` / `\d audit_events`, confirmando as FKs e os dois índices não únicos.

## 3. Infraestrutura compartilhada desta change

- [x] 3.1 Implementar `Common/Errors/LoanErrors.cs`: `NotFound`, `NotActive`, `NoCopyAvailable`, `BookInactive`, `BookNotFound`, `UserNotFound`, `IdempotencyKeyRequired`, `ValidationFailed(string detail)`, cada um mapeando para o status/`type` já fixados na tabela Contrato HTTP do `CLAUDE.md` (nenhuma linha nova necessária; `ValidationFailed` segue o mesmo padrão de `BookErrors.ValidationFailed`, usado por `CreateLoan` para `BookId`/`UserId` igual a `Guid.Empty`).
- [x] 3.2 Implementar `Features/Loans/LoanOptions.cs` (`DueDays`, padrão 14) e registrar via `IOptions`/config em `Program.cs`; valor configurável por `appsettings`/env var, nunca hardcoded no handler.
- [x] 3.3 Implementar `Features/Loans/RequireIdempotencyKeyFilter.cs` (`IEndpointFilter`): rejeita com `idempotency-key-required` (400) se o header `Idempotency-Key` estiver ausente/vazio; aplicado só ao `POST /loans`. Teste unitário do filtro isolado (sem `ApiFixture`).
- [x] 3.4 Mover `DefaultPage`/`DefaultPageSize`/`MaxPageSize` de `Features/Books/ListBooks.cs` para `Common/Pagination/PaginationDefaults.cs`; atualizar `ListBooks.cs` para referenciar a nova localização (sem mudar comportamento — mesmos valores, `dotnet test` da suíte de `catalog-endpoints` continua verde).

## 4. Casos de uso de Loan

Cada handler abaixo recebe `CancellationToken` do endpoint e o propaga até a última chamada de `AppDbContext`. Toda escrita bem-sucedida abre uma transação explícita cobrindo `ExecuteUpdateAsync` + inserts + `SaveChangesAsync`, commitada só no final.

- [x] 4.1 Implementar `Features/Loans/Contracts/LoanResponse.cs` com os campos `Id, BookId, UserId, Status, LoanedAtUtc, DueAtUtc, ReturnedAtUtc, CancelledAtUtc` (`From(entidade)`, `Status` serializado como string) e `Features/Loans/Contracts/PagedLoanResponse.cs` (`IReadOnlyList<LoanResponse> Items, int Page, int PageSize, int TotalCount`, `From(PagedResult<Loan>)`) — usado por `GetBookHistory` (5.1) e `GetUserLoans` (5.2), já que o item paginado é `Loan` em ambos, não faz sentido duplicar em `Features/Books`/`Features/Users`.
- [x] 4.2 Implementar `Features/Loans/CreateLoan.cs`: `CreateLoanRequest(Guid BookId, Guid UserId)`; rejeita com `validation-failed` (400) se `BookId`/`UserId` forem `Guid.Empty`; lê livro e usuário (404 se ausentes), tenta `ExecuteUpdateAsync` condicional (`IsActive && AvailableCopies > 0`) decrementando o livro; `affected == 0` mapeia para `book-inactive` ou `no-copy-available` a partir do snapshot lido antes; sucesso insere `Loan` (`DueAtUtc` via `LoanOptions.DueDays`) e `AuditEvent` (`LoanCreated`) na mesma transação; retorna 201 com `Location: /loans/{id}` e `LoanResponse` no corpo.
- [x] 4.3 Implementar `Features/Loans/ReturnLoan.cs`: lê o empréstimo (404 se ausente); `ExecuteUpdateAsync` condicional (`Status == Active`) setando `Returned`/`ReturnedAtUtc`; `affected == 0` (empréstimo existe mas não ativo) mapeia para `loan-not-active`; sucesso incrementa `available_copies` do livro (sem condição adicional) e insere `AuditEvent` (`LoanReturned`) na mesma transação — funciona mesmo com o livro desativado; retorna 200 com `LoanResponse` atualizado.
- [x] 4.4 Implementar `Features/Loans/CancelLoan.cs`: mesmo desenho de `ReturnLoan.cs`, com `CancelledAtUtc`/evento `LoanCancelled`; retorna 200 com `LoanResponse` atualizado.
- [x] 4.5 Implementar `Features/Loans/LoansEndpoints.cs` (`MapGroup("/loans")`, `RequireIdempotencyKeyFilter` só no `POST /loans`) e registrar em `Program.cs`.

## 5. Consultas de histórico

- [x] 5.1 Implementar `Features/Books/GetBookHistory.cs` (`GET /books/{id}/history`): 404 `book-not-found` se o livro não existir; página do histórico completo (`Active`/`Returned`/`Cancelled`) ordenado por `LoanedAtUtc` desc, usando `PaginationDefaults` (3.4) e retornando `PagedLoanResponse` (4.1); registrar em `BooksEndpoints`.
- [x] 5.2 Implementar `Features/Users/GetUserLoans.cs` (`GET /users/{id}/loans`) e `Features/Users/UsersEndpoints.cs` (`MapGroup("/users")`, primeira vez que a pasta existe): 404 `user-not-found` se o usuário não existir; mesma paginação e `PagedLoanResponse` de 5.1; registrar em `Program.cs`.

## 6. Testes de integração

- [x] 6.1 `CreateLoan`: corpo válido cria empréstimo ativo com `DueAtUtc` correto e 201+`Location`; `BookId`/`UserId` ausente ou `Guid.Empty` → 400 `validation-failed` sem criar empréstimo; `Idempotency-Key` ausente → 400; livro inexistente → 404 `book-not-found` sem alterar `available_copies` de nenhum livro; usuário inexistente → 404 `user-not-found`; livro inativo → 409 `book-inactive` sem consumir exemplar nem criar empréstimo; sem exemplar disponível → 409 `no-copy-available` sem consumir exemplar nem criar empréstimo; mesmo usuário pode ter dois empréstimos ativos do mesmo livro.
- [x] 6.2 `ReturnLoan`: empréstimo ativo → sucesso e incremento do contador; livro desativado depois do empréstimo → devolução ainda funciona; já devolvido → 409 `loan-not-active`; cancelado → 409 `loan-not-active`; inexistente → 404 `loan-not-found`.
- [x] 6.3 `CancelLoan`: mesmos cinco cenários de 6.2, adaptados para cancelamento; conferir que o registro do empréstimo continua existindo (nunca apagado) em todos os casos.
- [x] 6.4 Auditoria: criação/devolução/cancelamento bem-sucedidos gravam o `AuditEvent` correspondente (ator do header `X-Actor` ou `"anonymous"`, `correlationId` da requisição); uma rejeição de negócio (qualquer um dos 409/404 acima) não grava evento nenhum.
- [x] 6.5 `GetBookHistory`/`GetUserLoans`: retornam página com empréstimos nos três estados; id inexistente → 404 (`book-not-found`/`user-not-found`).
- [x] 6.6 Teste do último exemplar: `Barrier` com N=20, livro com 1 exemplar, uma `Idempotency-Key` distinta por requisição, `HttpClient` por tarefa (via `ApiFixture`); asserções: exatamente um 201, N-1 conflitos `no-copy-available`, `available_copies == 0` ao final, exatamente um empréstimo ativo para o livro.

## 7. Verificação final

- [x] 7.1 `dotnet build` nos 3 projetos sem warnings; `dotnet test` unitários e de integração passando (rodar a suíte de integração pelo menos 2x seguidas para checar estabilidade, dada a natureza concorrente da task 6.6); `openspec validate add-loan-concurrency --strict` válido.
