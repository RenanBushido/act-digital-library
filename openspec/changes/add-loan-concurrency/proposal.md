## Why

O catálogo de livros já tem domínio, persistência e endpoints HTTP (`catalog-domain`, `catalog-endpoints`), mas não existe nenhum jeito de emprestar um livro. Esta change fecha o ciclo transacional central do desafio: criar, devolver e cancelar empréstimos com garantia de consistência sob concorrência (múltiplas réplicas disputando o último exemplar), mais a trilha de auditoria dessas três operações.

## What Changes

- Endpoints: `POST /loans`, `POST /loans/{id}/return`, `POST /loans/{id}/cancel`, `GET /users/{id}/loans`, `GET /books/{id}/history` (ambos paginados, incluindo devolvidos/cancelados).
- Entidade `Loan`: criação, devolução e cancelamento como transições de estado explícitas, com invariantes no construtor/métodos (não devolver/cancelar o que já não está ativo).
- Constraint de concorrência: `POST /loans` decrementa `available_copies` via `ExecuteUpdateAsync` condicional (mesma transação do insert do `Loan` e do `AuditEvent`) — `affected == 0` é rejeição de negócio, nunca exceção. Devolução/cancelamento seguem o mesmo desenho (update condicional na própria linha do `Loan`, depois incremento do contador).
- `POST /loans` exige o header `Idempotency-Key` — **nesta change, só a presença é validada** (400 se ausente); a deduplicação de fato (tabela `idempotency_keys`, `IEndpointFilter`) fica para a próxima change.
- Entidade `AuditEvent` e tabela `audit_events`: evento `LoanCreated`/`LoanReturned`/`LoanCancelled` gravado na mesma transação do fato, nunca via `ILogger`.
- Nova migration adicionando `loans` e `audit_events`.
- Teste de integração do último exemplar: `Barrier` com N=20 requisições concorrentes, livro com 1 exemplar, uma `Idempotency-Key` distinta por requisição — exatamente um 201, N-1 conflitos de negócio, `available_copies == 0`, um único empréstimo ativo.

## Capabilities

### New Capabilities
- `loan-management`: ciclo de vida do empréstimo (criação, devolução, cancelamento) com garantia de consistência sob concorrência, auditoria das três operações, e consulta paginada do histórico por usuário e por livro.

### Modified Capabilities
(nenhuma — `catalog-domain` e `catalog-endpoints` não mudam de comportamento; `Book`/`User` são só lidos, não alterados em forma)

## Impact

- Novo `src/Library.Api/Domain/Loan/Loan.cs` e `src/Library.Api/Domain/Audit/AuditEvent.cs` (entidades com invariantes).
- Novo `src/Library.Api/Features/Loans/` (`CreateLoan.cs`, `ReturnLoan.cs`, `CancelLoan.cs`, `LoansEndpoints.cs`, `Contracts/LoanResponse.cs`) e `src/Library.Api/Features/Books/GetBookHistory.cs` (estende `BooksEndpoints`).
- Novo `src/Library.Api/Features/Users/` (`GetUserLoans.cs`, `UsersEndpoints.cs`) — primeira vez que a pasta `Users` (endpoints) existe; `Domain/User` já existe desde `catalog-domain`.
- Novo `src/Library.Api/Infrastructure/Persistence/LoanConfiguration.cs` e `AuditEventConfiguration.cs`.
- Nova migration em `src/Library.Api/Migrations` (tabelas `loans`, `audit_events`; sem mudança nas tabelas existentes).
- Fora do escopo (Non-Goal, registrado em `design.md`): deduplicação idempotente de verdade, cache, métricas OpenTelemetry, health checks, eventos de auditoria de `Book`, `GET /audit-events`.
