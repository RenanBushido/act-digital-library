## Purpose

Define o ciclo de vida do empréstimo (criação, devolução, cancelamento) com garantia de consistência sob concorrência, a trilha de auditoria dessas três operações, e a consulta paginada do histórico de empréstimos por usuário e por livro.

## ADDED Requirements

### Requirement: Create loan requires an Idempotency-Key header
`POST /loans` SHALL exigir o header `Idempotency-Key`. Nesta capability, apenas a presença do header é validada — a deduplicação de requisições repetidas é responsabilidade de uma capability futura.

#### Scenario: Missing Idempotency-Key is rejected
- **GIVEN** uma requisição a `POST /loans` sem o header `Idempotency-Key`
- **WHEN** a requisição é processada
- **THEN** a resposta é 400 `problem+json` com `type: idempotency-key-required`, e nenhum empréstimo é criado

### Requirement: Create loan validates book and user
`POST /loans` SHALL só criar o empréstimo se o livro existir, estiver ativo e tiver exemplar disponível, e o usuário existir. Uma rejeição de negócio não altera `available_copies` nem cria o empréstimo.

#### Scenario: Valid request creates an active loan
- **GIVEN** um livro ativo com exemplar disponível e um usuário existente, com `Idempotency-Key` presente
- **WHEN** um cliente faz `POST /loans`
- **THEN** a resposta é 201 com `Location` apontando para o empréstimo criado, `available_copies` do livro decrementado em 1, e o empréstimo criado com data de empréstimo e data prevista de devolução (14 dias depois, por padrão)

#### Scenario: Invalid body is rejected
- **GIVEN** um corpo com `bookId` ou `userId` ausente ou igual a `Guid.Empty`
- **WHEN** um cliente faz `POST /loans`
- **THEN** a resposta é 400 `problem+json` com `type: validation-failed`, e nenhum empréstimo é criado

#### Scenario: Nonexistent book is rejected
- **GIVEN** um id de livro que não existe
- **WHEN** um cliente faz `POST /loans` referenciando esse id
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`, e `available_copies` de nenhum livro é alterado

#### Scenario: Nonexistent user is rejected
- **GIVEN** um id de usuário que não existe
- **WHEN** um cliente faz `POST /loans` referenciando esse id
- **THEN** a resposta é 404 `problem+json` com `type: user-not-found`, e nenhum empréstimo é criado

#### Scenario: Inactive book is rejected without consuming a copy
- **GIVEN** um livro desativado (`is_active == false`), mesmo com exemplares disponíveis
- **WHEN** um cliente faz `POST /loans` referenciando esse livro
- **THEN** a resposta é 409 `problem+json` com `type: book-inactive`, `available_copies` do livro não muda, e nenhum empréstimo é criado

#### Scenario: No copy available is rejected without consuming a copy
- **GIVEN** um livro ativo com `available_copies == 0`
- **WHEN** um cliente faz `POST /loans` referenciando esse livro
- **THEN** a resposta é 409 `problem+json` com `type: no-copy-available`, `available_copies` do livro permanece 0, e nenhum empréstimo é criado

#### Scenario: The same user can have more than one active loan of the same book
- **GIVEN** um usuário com um empréstimo ativo de um livro com mais de um exemplar disponível
- **WHEN** o mesmo usuário faz `POST /loans` para o mesmo livro novamente
- **THEN** a resposta é 201 e um segundo empréstimo ativo é criado para o mesmo usuário e livro

### Requirement: Loan creation is atomic under concurrency
Com um único exemplar disponível, N requisições `POST /loans` concorrentes para o mesmo livro SHALL produzir exatamente um empréstimo bem-sucedido e N-1 rejeições de regra de negócio, sem que `available_copies` fique negativo e sem empréstimo duplicado, independentemente do número de réplicas da aplicação.

#### Scenario: Last copy under concurrent load
- **GIVEN** um livro ativo com exatamente 1 exemplar disponível
- **WHEN** 20 requisições `POST /loans` para esse livro chegam concorrentemente, cada uma com uma `Idempotency-Key` distinta
- **THEN** exatamente uma responde 201, as outras 19 respondem 409 `no-copy-available`, `available_copies` do livro termina em 0, e existe exatamente um empréstimo ativo para esse livro

### Requirement: Return loan
`POST /loans/{id}/return` SHALL só devolver um empréstimo ativo, incrementando `available_copies` do livro e registrando a data de devolução — mesmo que o livro tenha sido desativado depois do empréstimo.

#### Scenario: Returning an active loan succeeds
- **GIVEN** um empréstimo ativo
- **WHEN** um cliente faz `POST /loans/{id}/return`
- **THEN** a resposta é 200 com o empréstimo no corpo, `Status: "Returned"`, data de devolução registrada, e `available_copies` do livro incrementado em 1

#### Scenario: Returning works even if the book was deactivated
- **GIVEN** um empréstimo ativo cujo livro foi desativado depois da criação do empréstimo
- **WHEN** um cliente faz `POST /loans/{id}/return`
- **THEN** a devolução é aceita normalmente, incrementando `available_copies` do livro desativado

#### Scenario: Returning an already returned loan is rejected
- **GIVEN** um empréstimo já devolvido
- **WHEN** um cliente faz `POST /loans/{id}/return` novamente
- **THEN** a resposta é 409 `problem+json` com `type: loan-not-active`, e `available_copies` não muda

#### Scenario: Returning a cancelled loan is rejected
- **GIVEN** um empréstimo cancelado
- **WHEN** um cliente faz `POST /loans/{id}/return`
- **THEN** a resposta é 409 `problem+json` com `type: loan-not-active`

#### Scenario: Nonexistent loan returns 404
- **GIVEN** nenhum empréstimo com o id informado
- **WHEN** um cliente faz `POST /loans/{id}/return`
- **THEN** a resposta é 404 `problem+json` com `type: loan-not-found`

### Requirement: Cancel loan
`POST /loans/{id}/cancel` SHALL só cancelar um empréstimo ativo, devolvendo o exemplar ao estoque e registrando a data de cancelamento. O registro do empréstimo é preservado — nunca apagado.

#### Scenario: Cancelling an active loan succeeds
- **GIVEN** um empréstimo ativo
- **WHEN** um cliente faz `POST /loans/{id}/cancel`
- **THEN** a resposta é 200 com o empréstimo no corpo, `Status: "Cancelled"`, data de cancelamento registrada, `available_copies` do livro incrementado em 1, e o registro do empréstimo continua existindo (nunca apagado)

#### Scenario: Cancelling an already cancelled loan is rejected
- **GIVEN** um empréstimo já cancelado
- **WHEN** um cliente faz `POST /loans/{id}/cancel` novamente
- **THEN** a resposta é 409 `problem+json` com `type: loan-not-active`, e `available_copies` não muda

#### Scenario: Cancelling a returned loan is rejected
- **GIVEN** um empréstimo já devolvido
- **WHEN** um cliente faz `POST /loans/{id}/cancel`
- **THEN** a resposta é 409 `problem+json` com `type: loan-not-active`

#### Scenario: Nonexistent loan returns 404
- **GIVEN** nenhum empréstimo com o id informado
- **WHEN** um cliente faz `POST /loans/{id}/cancel`
- **THEN** a resposta é 404 `problem+json` com `type: loan-not-found`

### Requirement: Loan operations are audited
Cada criação, devolução e cancelamento de empréstimo bem-sucedidos SHALL gravar um evento de auditoria (`LoanCreated`, `LoanReturned`, `LoanCancelled`) na mesma transação do fato, nunca via `ILogger`. Uma rejeição de negócio não gera evento de auditoria.

#### Scenario: Successful creation is audited
- **GIVEN** uma requisição válida de `POST /loans`
- **WHEN** o empréstimo é criado com sucesso
- **THEN** um evento `LoanCreated` é gravado com o id do empréstimo, o ator (header `X-Actor`, ou `"anonymous"` se ausente), o timestamp UTC e o identificador de correlação da requisição

#### Scenario: Rejected creation is not audited
- **GIVEN** uma requisição de `POST /loans` que é rejeitada por regra de negócio (livro inativo, sem exemplar, livro ou usuário inexistente)
- **WHEN** a requisição é processada
- **THEN** nenhum evento de auditoria é gravado

#### Scenario: Successful return and cancellation are audited
- **GIVEN** um empréstimo ativo
- **WHEN** ele é devolvido ou cancelado com sucesso
- **THEN** um evento `LoanReturned` ou `LoanCancelled`, respectivamente, é gravado na mesma transação, com ator, timestamp UTC e identificador de correlação

### Requirement: Get user loan history
`GET /users/{id}/loans` SHALL retornar o histórico paginado de empréstimos de um usuário, incluindo ativos, devolvidos e cancelados.

#### Scenario: Existing user returns full paginated history
- **GIVEN** um usuário com empréstimos ativos, devolvidos e cancelados
- **WHEN** um cliente faz `GET /users/{id}/loans`
- **THEN** a resposta é 200 com uma página do histórico completo, incluindo os três estados

#### Scenario: Nonexistent user returns 404
- **GIVEN** nenhum usuário com o id informado
- **WHEN** um cliente faz `GET /users/{id}/loans`
- **THEN** a resposta é 404 `problem+json` com `type: user-not-found`

### Requirement: Get book loan history
`GET /books/{id}/history` SHALL retornar o histórico paginado de empréstimos de um livro, incluindo ativos, devolvidos e cancelados.

#### Scenario: Existing book returns full paginated history
- **GIVEN** um livro com empréstimos ativos, devolvidos e cancelados
- **WHEN** um cliente faz `GET /books/{id}/history`
- **THEN** a resposta é 200 com uma página do histórico completo, incluindo os três estados

#### Scenario: Nonexistent book returns 404
- **GIVEN** nenhum livro com o id informado
- **WHEN** um cliente faz `GET /books/{id}/history`
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`
