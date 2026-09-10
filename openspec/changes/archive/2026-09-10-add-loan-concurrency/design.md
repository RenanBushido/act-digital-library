## Context

`catalog-domain` fixou `Book`/`User` e o schema; `catalog-endpoints` expôs o catálogo de livros via HTTP. Nenhum dos dois toca em empréstimo. O `CLAUDE.md` já fixa, antes desta change existir, o desenho de concorrência ("Empréstimo = uma transação, três escritas"), o schema de auditoria (`audit_events`), a tabela completa de erros HTTP (todas as linhas usadas por esta change já existem: `book-not-found`, `user-not-found`, `loan-not-found`, `no-copy-available`, `book-inactive`, `loan-not-active`, `idempotency-key-required`) e o teste do último exemplar (`Barrier`, N=20). Este `design.md` decide como implementar isso em cima do que já existe — `AppDbContext`, `Book`, `User`, `Common/Errors`, `Common/CorrelationIdMiddleware` — sem reabrir nenhuma dessas decisões.

## Goals / Non-Goals

**Goals:**
- `POST /loans`, `POST /loans/{id}/return`, `POST /loans/{id}/cancel` com a garantia de atomicidade descrita em Concorrência do `CLAUDE.md`.
- `GET /users/{id}/loans` e `GET /books/{id}/history`, paginados, incluindo todos os estados de empréstimo.
- Evento de auditoria (`LoanCreated`/`LoanReturned`/`LoanCancelled`) na mesma transação de cada operação bem-sucedida.
- Teste de integração do último exemplar exatamente como o `CLAUDE.md` especifica (`Barrier`, N=20, uma `Idempotency-Key` por requisição).

**Non-Goals:**
- Deduplicação idempotente de verdade (tabela `idempotency_keys`, `IEndpointFilter` completo) — só a presença do header é validada nesta change.
- Cache Redis em qualquer leitura desta change (`GET /users/{id}/loans`, `GET /books/{id}/history`) — o `CLAUDE.md` só autoriza cache em `GET /books` e `GET /books/{id}/availability`, ambos de `catalog-endpoints`.
- Métricas OpenTelemetry (`Meter Library.Loans`) e health checks — nenhum dos dois depende de `Loan` existir para outra capability funcionar; ficam para uma change de observabilidade.
- Eventos de auditoria de `Book` (`BookCreated`/`BookUpdated`/`BookDeactivated`) e `GET /audit-events` — só os três eventos de `Loan`.
- `GET /loans/{id}` — não faz parte do escopo declarado; ver decisão sobre `Location` abaixo.
- Unicidade de empréstimo ativo por usuário+livro — o mesmo usuário pode ter mais de um empréstimo ativo do mesmo livro; nenhuma constraint deve impedir isso.
- `book-has-active-loans` em `DeactivateBook` (`catalog-endpoints`) — o `design.md` daquela change registrou que o check deveria ser adicionado "quando `Loans` existir". Decisão desta change: **continua adiado**. O brief desta change (`docs/3 - add-loan-concurrency.md`) não pede alteração em `DeactivateBook`, e implementá-lo aqui faria `Features/Books` depender de dados de `Features/Loans` — acoplamento cruzado não solicitado. Fica registrado explicitamente (não mais um silêncio) para uma change futura que queira reforçar essa regra.

## Decisions

### Alternativas de concorrência descartadas

O `CLAUDE.md` já fixa a estratégia (`UPDATE` condicional via `ExecuteUpdateAsync`, isolamento `READ COMMITTED`). Para não reabrir essa decisão a cada change, seguem as alternativas descartadas e por quê:

- **Lock pessimista com `FOR UPDATE`**: exigiria manter uma transação aberta durante todo o processamento da requisição (SELECT ... FOR UPDATE, depois UPDATE), aumentando o tempo de retenção de lock sob concorrência e reintroduzindo o padrão "leitura-depois-escrita" que o `CLAUDE.md` explicitamente evita. `ExecuteUpdateAsync` condicional faz a decisão e a escrita na mesma instrução, sem SELECT prévio bloqueante.
- **Token otimista com `xmin`**: exige um retry loop no cliente da aplicação (ler, tentar escrever, comparar `xmin`, tentar de novo em conflito) — sob 20 requisições disputando 1 exemplar, a maioria teria que retry pelo menos uma vez, aumentando latência e complexidade sem ganho sobre o `UPDATE` condicional, que resolve em uma única instrução.
- **Isolamento `SERIALIZABLE`**: sob alta contenção (20 requisições no mesmo livro) geraria serialization failures que precisariam de retry na aplicação — o mesmo custo do otimista, mas com o overhead adicional do próprio nível de isolamento em todas as transações do sistema, não só nesta.
- **Advisory lock do Postgres**: resolveria a atomicidade, mas é essencialmente um lock pessimista disfarçado — mesma retenção de lock, e adiciona uma primitiva (`pg_advisory_xact_lock`) que não teria nenhum outro uso no projeto.
- **Lock distribuído no Redis**: proibido explicitamente pelo `CLAUDE.md` ("Lock distribuído no Redis para decidir empréstimo"). Redis não é a fonte da verdade deste projeto, e um lock ali não impede duas réplicas de escreverem no Postgres se o lock falhar/expirar — a garantia real só existe no banco.

### Transação explícita cobrindo as três escritas

`ExecuteUpdateAsync` cada instrução com um `COMMIT` implícito próprio — para as três escritas (`ExecuteUpdateAsync` no livro, insert do `Loan`, insert do `AuditEvent`) ficarem atômicas entre si, o handler abre uma transação explícita (`await dbContext.Database.BeginTransactionAsync(cancellationToken)`), roda o `ExecuteUpdateAsync` condicional, checa `affected`, adiciona `Loan`+`AuditEvent` ao `DbContext`, chama `SaveChangesAsync`, e só então `CommitAsync`. Se `affected == 0`, a transação é descartada (`RollbackAsync` ou simplesmente não commitada) e a rejeição de negócio é retornada sem side effect. Devolução e cancelamento seguem o mesmo desenho: `ExecuteUpdateAsync` condicional na própria linha do `Loan` (`Where(l => l.Id == id && l.Status == LoanStatus.Active)`, setando `Status`/`ReturnedAtUtc` ou `CancelledAtUtc`) decide atomicamente se a transição de estado é válida — `affected == 0` distingue "não existe" (checado antes, 404) de "existe mas não está ativo" (409 `loan-not-active`) — e só then o contador do livro é incrementado (sem condição adicional, já que a linha do empréstimo garantiu que a transição só acontece uma vez).

### Corpo de `POST /loans`

`CreateLoanRequest(Guid BookId, Guid UserId)`. `Guid` não tem uma faixa "vazia" capturável por `[Required]`/`AddValidation()` nativo — o handler valida explicitamente `BookId == Guid.Empty || UserId == Guid.Empty` e retorna `validation-failed` (400) antes de consultar o banco, mesmo padrão de `CreateBook` (`DomainException` → `BookErrors.ValidationFailed`). Corpo malformado (JSON inválido, campo de tipo errado) já vira 400 automaticamente pelo model binding nativo do Minimal API, sem código extra.

### `Return`/`Cancel` respondem 200 com o empréstimo atualizado

Ao contrário de `DeactivateBook` (204, nada de novo para devolver), `Return`/`Cancel` têm dado novo relevante para o cliente (data de devolução/cancelamento, `Status` atualizado) — resposta 200 com `LoanResponse`, não 204.

### `LoanResponse`

`LoanResponse(Guid Id, Guid BookId, Guid UserId, string Status, DateTimeOffset LoanedAtUtc, DateTimeOffset DueAtUtc, DateTimeOffset? ReturnedAtUtc, DateTimeOffset? CancelledAtUtc)`, com `From(Loan)`. `Status` serializado como string (nome do enum), consistente com o `HasConversion<string>()` da persistência.

### `PagedLoanResponse` mora em `Features/Loans/Contracts/`, não duplicado

`GetBookHistory` (`Features/Books`) e `GetUserLoans` (`Features/Users`) retornam a mesma forma de página — uma lista de `LoanResponse` + `Page`/`PageSize`/`TotalCount`, espelhando `PagedBookResponse.From(PagedResult<Book>)`. Em vez de duplicar esse wrapper em duas features (ou criar uma dependência estranha de uma feature na outra), `PagedLoanResponse` mora em `Features/Loans/Contracts/` — o item paginado é `Loan`, então a responsabilidade é de `Loans`, e tanto `GetBookHistory` quanto `GetUserLoans` importam de lá.

### FK de `loans` para `books`/`users`

`LoanConfiguration` declara `HasOne<Book>().WithMany().HasForeignKey(l => l.BookId)` e o equivalente para `User` — sem propriedade de navegação em `Loan` (relacionamento "sombra", só para a constraint), mantendo a entidade enxuta. `Book`/`User` nunca são apagados fisicamente, então não há risco de exigir `ON DELETE CASCADE`/`SET NULL`; a FK é só a garantia de integridade referencial que faltava.

### Índices em `book_id` e `user_id`

`LoanConfiguration` adiciona `HasIndex(l => l.BookId)` e `HasIndex(l => l.UserId)` (não únicos) — sustentam `GET /books/{id}/history` e `GET /users/{id}/loans`, que filtram exatamente por essas colunas.

### `Common/Pagination/PaginationDefaults.cs`

`ListBooks` (change anterior) tem `DefaultPage`/`DefaultPageSize`/`MaxPageSize` como constantes locais suas. Com `GetBookHistory` e `GetUserLoans` (duas features diferentes) precisando dos mesmos valores, essas constantes migram para `Common/Pagination/PaginationDefaults.cs` — `ListBooks` passa a referenciá-las também, em vez de manter a cópia local. É um ajuste pontual em um arquivo já existente (não uma refatoração oportunista maior): move três constantes, não muda comportamento, e evita `Features/Users` referenciar uma constante de `Features/Books`.

### `LoanStatus` como enum convertido para texto

`Loan.Status` é um enum (`Active`, `Returned`, `Cancelled`) mapeado com `HasConversion<string>()` — mais legível direto no banco (para quem for depurar `audit_events`/`loans` via `psql`) do que um inteiro, e evita a armadilha de reordenar valores de enum sem perceber que quebra dados já persistidos.

### `Location: /loans/{id}` sem `GET /loans/{id}`

Decisão do usuário: o 201 aponta para `/loans/{id}` mesmo sem um endpoint que resolva essa URL nesta change — o cliente já tem o empréstimo completo no corpo da resposta, e a convenção REST fica preparada para quando uma change futura adicionar `GET /loans/{id}`.

### `GET /users/{id}/loans` e `GET /books/{id}/history` validam a existência do recurso

Decisão do usuário: 404 (`user-not-found`/`book-not-found`) para id inexistente, em vez de lista vazia — consistente com `GET /books/{id}` (`catalog-endpoints`), que já trata o id como um recurso a validar, não um filtro solto.

### `AuditEvent` em `Domain/Audit/`, gravado pelo próprio handler

Sem um "serviço de auditoria": cada handler (`CreateLoan`, `ReturnLoan`, `CancelLoan`) monta um `AuditEvent.Create(...)` e adiciona ao mesmo `DbContext` antes do `SaveChangesAsync` que já está fazendo dentro da transação — é exatamente "grava um evento na mesma transação do fato", sem introduzir uma abstração (`IAuditService`) que o `CLAUDE.md` não pediu e que teria um único consumidor. O `payload` (jsonb) carrega antes/depois dos campos relevantes do empréstimo (ex.: `{ "status": { "before": "Active", "after": "Returned" } }`), não a entidade inteira, seguindo o padrão que `catalog-domain`/Auditoria do `CLAUDE.md` já definem para `Book`.

### Ator e correlação lidos por parâmetro do handler

`actor` vem de `[FromHeader(Name = "X-Actor")] string? actor` (default `"anonymous"` quando ausente/vazio, tratado no próprio handler). `correlationId` vem de `HttpContext.Items[CorrelationIdMiddleware.ItemKey]`, já populado pelo middleware existente — o handler recebe `HttpContext` como parâmetro (Minimal API injeta automaticamente), sem precisar de um novo mecanismo.

### `IEndpointFilter` para o check de `Idempotency-Key`, já preparando a próxima change

O `CLAUDE.md` fixa que a idempotência completa é "Implementado como `IEndpointFilter`, aplicado somente ao endpoint `POST /loans`". Esta change implementa só o check de presença (`Features/Loans/RequireIdempotencyKeyFilter.cs`) já como `IEndpointFilter`, para a change seguinte (dedup de verdade) estender o mesmo filtro em vez de descartar um check ad-hoc dentro do handler e escrever o filtro do zero.

### `DueDays` configurável via `appsettings`

Prazo padrão de 14 dias vem de `IConfiguration["Loans:DueDays"]` (ou `IOptions<LoanOptions>`), lido no handler de criação — não hardcoded, seguindo "Configuração por `appsettings.*.json` e variáveis de ambiente" do `CLAUDE.md`. Sem endpoint para o cliente informar o prazo, como o brief exige.

## Risks / Trade-offs

- [Snapshot do livro lido antes do `ExecuteUpdateAsync` pode ficar defasado sob corrida, gerando uma mensagem de erro (`book-inactive` vs. `no-copy-available`) que não reflete exatamente qual condição falhou no instante da escrita] → Aceito: a garantia de corretude (nunca decrementar abaixo de zero, nunca criar dois empréstimos pro último exemplar) vem inteiramente do `WHERE` condicional do `ExecuteUpdateAsync`, não da leitura prévia. Na pior hipótese sob corrida, o cliente recebe um 409 com o `type` "errado" (mas ainda um 409 correto) — não uma inconsistência de dado.
- [Transação explícita span duas `ExecuteUpdateAsync`/inserts sob `READ COMMITTED` pode, em tese, sofrer lost update se o `WHERE` condicional não for suficientemente específico] → Mitigação: o `WHERE` do decremento inclui a condição de negócio completa (`IsActive && AvailableCopies > 0`), igual ao que `catalog-domain` já valida no nível de constraint (`CHECK`); o `WHERE` do update de status do empréstimo inclui `Status == Active`. Ambos são a certeira de atomicidade, não a transação em si.
- [Nenhuma constraint impede duas linhas de `Loan` ativas para o mesmo usuário+livro] → Intencional, não um risco: o brief exige exatamente esse comportamento.
