## 1. Schema

- [x] 1.1 Alterar `Domain/Audit/AuditEvent.cs` para chave `long Id` gerada pelo banco (remover geração de `Guid` no `Create`) e ajustar `Infrastructure/Persistence/AuditEventConfiguration.cs` (`ValueGeneratedOnAdd`), adicionando os índices `(entity_type, entity_id, occurred_at_utc)` e `(correlation_id)`; verificar que `dotnet build` compila sem warnings (`TreatWarningsAsErrors`)
- [x] 1.2 Gerar a migration em `src/Library.Api/Migrations` para a nova PK e os índices; verificar com `dotnet ef migrations script` que o script recria a PK como `bigint identity` sem apagar as demais colunas
- [x] 1.3 Ajustar qualquer código que hoje trate `AuditEvent.Id` como `Guid` (contratos de resposta ainda não existem; checar `LoanResponse`/uso indireto) e ajustar `AuditEventTests.cs` (unitário existente) para o novo tipo de Id

## 2. Book audit events

- [x] 2.1 Em `Features/Books/CreateBook.cs`, após `SaveChangesAsync` bem-sucedido, gravar `BookCreated` na mesma transação (mesma chamada de `SaveChangesAsync`, adicionando `AuditEvent` ao mesmo `ChangeTracker` antes de salvar) com payload `{ "after": { ... } }` do estado inicial; verificar com teste de integração que o evento existe após 201 e que nenhum evento existe após rejeição (ISBN duplicado ou validação)
- [x] 2.2 Em `Features/Books/UpdateBook.cs`, calcular o payload de `BookUpdated` a partir dos valores já carregados em memória antes de chamar `UpdateDetails` (título, autor, `totalCopies`, `availableCopies`), incluindo apenas campos que mudaram, e gravar o evento na mesma `SaveChangesAsync`; verificar com teste de integração cobrindo mudança de quantidade (before/after de `totalCopies`/`availableCopies`), o caso de mudar apenas o título (payload com `before`/`after` só de `title`, sem `totalCopies`/`availableCopies`), e o caso sem mudança efetiva (payload sem entradas, mas evento gravado)
- [x] 2.3 Em `Features/Books/DeactivateBook.cs`, gravar `BookDeactivated` apenas quando `wasActive` era verdadeiro (reaproveitando o guard já existente para invalidação de cache), com payload `{ "before": { "isActive": true }, "after": { "isActive": false } }`; verificar com teste de integração que desativar um livro já inativo não grava um segundo evento
- [x] 2.4 Resolver `actor` (header `X-Actor`, padrão `"anonymous"`) e `correlationId` (via `CorrelationIdMiddleware.ItemKey`, seguindo o padrão já usado em `CreateLoan.cs`) nos três handlers acima

## 3. Audit trail query endpoint

- [x] 3.1 Criar `Features/Audit/Contracts/AuditEventResponse.cs` com factory `From(AuditEvent)` e `Features/Audit/Contracts/PagedAuditEventResponse.cs` seguindo o padrão de `PagedBookResponse`/`PagedLoanResponse`
- [x] 3.2 Criar `Features/Audit/GetAuditEvents.cs` com filtros opcionais (`entityType`, `entityId`, `action`, `actor`, `correlationId`, `from`/`to` sobre `occurred_at_utc`), paginação via `PaginationDefaults`, e ordenação por `occurred_at_utc DESC, Id DESC` como critério de desempate; verificar com teste de integração cada filtro isoladamente e a combinação que não retorna nada
- [x] 3.3 Criar `Features/Audit/AuditEventsEndpoints.cs` com `MapGroup("/audit-events")` expondo `GET /audit-events`, registrado em `Program.cs`; verificar com teste de integração que o grupo responde 200
- [x] 3.4 Teste de integração para paginação determinística com dois eventos no mesmo `occurred_at_utc` (usar `TimeProvider` fake com o mesmo instante para duas operações), confirmando que paginar não duplica nem omite eventos

## 4. Correlation cross-cutting

- [x] 4.1 Teste de integração confirmando que uma requisição a `POST /books`, `PATCH /books/{id}` e `DELETE /books/{id}` com `X-Correlation-Id` enviado grava esse valor no evento de auditoria correspondente
- [x] 4.2 Teste de integração confirmando que, sem `X-Correlation-Id` enviado, o valor gerado pelo servidor (presente no header de resposta) é o mesmo gravado no evento de auditoria
- [x] 4.3 Teste de integração confirmando que as duas escritas de uma mesma requisição de `POST /loans` (decremento de `available_copies` e `AuditEvent`) já compartilham o mesmo `correlation_id` (comportamento existente, agora coberto pela spec `audit-trail`)
- [x] 4.4 Teste de integração confirmando que `GET /audit-events` sem `X-Correlation-Id` enviado ainda devolve um valor gerado no header de resposta, mesmo sem gravar nenhum evento de auditoria — cobre a propagação em endpoint somente leitura

## 5. Regressão dos eventos de empréstimo

- [x] 5.1 Teste de integração criando, devolvendo e cancelando um empréstimo após a migration da tarefa 1.2, e confirmando via `GET /audit-events` que `LoanCreated`, `LoanReturned` e `LoanCancelled` continuam sendo gravados e lidos corretamente com o novo tipo de `Id` (`bigint`) — cobre a regressão da change `add-loan-concurrency`

## 6. Verificação final

- [x] 6.1 Rodar a suíte completa (`dotnet test`) com Testcontainers e confirmar que todo cenário GIVEN/WHEN/THEN das specs `audit-trail` (nova) e `catalog-endpoints` (delta) tem um teste correspondente
- [x] 6.2 Rodar `openspec validate add-domain-audit --strict` e confirmar que passa sem erros
