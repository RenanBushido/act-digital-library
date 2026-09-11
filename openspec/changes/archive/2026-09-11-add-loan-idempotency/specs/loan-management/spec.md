## MODIFIED Requirements

### Requirement: Create loan requires an Idempotency-Key header
`POST /loans` SHALL exigir o header `Idempotency-Key`. Além da presença, a chave é deduplicada: repetir a mesma chave com o mesmo corpo nunca cria um segundo empréstimo nem reduz `available_copies` uma segunda vez; a mesma chave com corpo diferente é rejeitada; uma segunda requisição com a mesma chave enquanto a primeira ainda está em processamento é rejeitada; e apenas respostas de sucesso (2xx) são armazenadas para replay — uma rejeição de negócio ou exceção libera a chave para uma nova tentativa legítima ser reavaliada do zero.

#### Scenario: Missing Idempotency-Key is rejected
- **GIVEN** uma requisição a `POST /loans` sem o header `Idempotency-Key`
- **WHEN** a requisição é processada
- **THEN** a resposta é 400 `problem+json` com `type: idempotency-key-required`, e nenhum empréstimo é criado

#### Scenario: Repeating a successful request replays the original response
- **GIVEN** uma `POST /loans` bem-sucedida (201) com uma `Idempotency-Key` e um corpo específicos
- **WHEN** o cliente repete exatamente a mesma requisição (mesma `Idempotency-Key`, mesmo corpo)
- **THEN** a resposta tem o mesmo status e o mesmo corpo da requisição original, acrescido do header `Idempotency-Replayed: true`, nenhum segundo empréstimo é criado, e `available_copies` não é reduzido novamente

#### Scenario: Same key with a different body is rejected
- **GIVEN** uma `POST /loans` bem-sucedida com uma `Idempotency-Key`
- **WHEN** o cliente repete a mesma `Idempotency-Key` com um corpo diferente (por exemplo, outro `bookId` ou `userId`)
- **THEN** a resposta é 422 `problem+json` com `type: idempotency-key-reuse`, o empréstimo original permanece intacto, e nenhum novo empréstimo é criado

#### Scenario: A request with the same key currently in flight is rejected
- **GIVEN** uma `POST /loans` com uma `Idempotency-Key` cujo processamento ainda não terminou (a chave foi reservada mas a resposta ainda não foi gravada)
- **WHEN** uma segunda requisição chega com a mesma `Idempotency-Key`
- **THEN** a resposta é 409 `problem+json` com `type: request-in-flight`, e nenhum empréstimo é criado por essa segunda requisição

#### Scenario: A business rejection releases the key for retry
- **GIVEN** uma `POST /loans` com uma `Idempotency-Key` que é rejeitada por regra de negócio (livro inativo, sem exemplar disponível, livro ou usuário inexistente)
- **WHEN** o cliente repete a mesma `Idempotency-Key` com o mesmo corpo após a condição de rejeição deixar de existir (por exemplo, um exemplar foi devolvido)
- **THEN** a nova tentativa é reavaliada do zero e pode ser bem-sucedida (201), em vez de repetir a rejeição original ou retornar `request-in-flight`

#### Scenario: Concurrent requests with the same key produce exactly one loan
- **GIVEN** um livro ativo com exemplar disponível
- **WHEN** N requisições `POST /loans` concorrentes chegam com a mesma `Idempotency-Key` e o mesmo corpo
- **THEN** exatamente uma delas cria o empréstimo (201) e decrementa `available_copies` em 1, e todas as demais recebem o replay da resposta (`Idempotency-Replayed: true`) ou 409 `request-in-flight` — nunca um segundo empréstimo

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
- **WHEN** o mesmo usuário faz `POST /loans` para o mesmo livro novamente, com uma `Idempotency-Key` diferente
- **THEN** a resposta é 201 e um segundo empréstimo ativo é criado para o mesmo usuário e livro

### Requirement: Loan creation is atomic under concurrency
Com um único exemplar disponível, N requisições `POST /loans` concorrentes para o mesmo livro, cada uma com uma `Idempotency-Key` distinta, SHALL produzir exatamente um empréstimo bem-sucedido e N-1 rejeições de regra de negócio, sem que `available_copies` fique negativo e sem empréstimo duplicado, independentemente do número de réplicas da aplicação. Este requisito cobre chaves distintas competindo pelo mesmo exemplar; a deduplicação de requisições com a mesma chave é coberta pelo requisito anterior.

#### Scenario: Last copy under concurrent load
- **GIVEN** um livro ativo com exatamente 1 exemplar disponível
- **WHEN** 20 requisições `POST /loans` para esse livro chegam concorrentemente, cada uma com uma `Idempotency-Key` distinta
- **THEN** exatamente uma responde 201, as outras 19 respondem 409 `no-copy-available`, `available_copies` do livro termina em 0, e existe exatamente um empréstimo ativo para esse livro
