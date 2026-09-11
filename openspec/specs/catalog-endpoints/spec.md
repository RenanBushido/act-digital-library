# Catalog Endpoints Specification

## Purpose

Define o contrato HTTP de leitura e escrita do catálogo de livros (`Book`): as respostas, os erros e as garantias de cache que os endpoints `GET/POST/PATCH/DELETE /books` precisam cumprir.

## Requirements

### Requirement: List books
`GET /books` SHALL retornar uma lista paginada de livros ativos, com leitura passando por cache Redis. `GET /books` aceita os filtros opcionais `title`, `author`, `isbn` e `includeInactive`, além de `page` e `pageSize`. A chave de cache cobre a combinação normalizada de todos esses parâmetros, de forma que combinações diferentes de filtro/paginação são cacheadas separadamente. Um valor divergente gravado no cache não influencia a criação de empréstimo: a decisão de disponibilidade para `POST /loans` vem sempre do PostgreSQL, nunca da leitura cacheada por este endpoint.

#### Scenario: List returns paginated books
- **GIVEN** livros existentes no catálogo
- **WHEN** um cliente faz `GET /books` sem parâmetros
- **THEN** a resposta é 200 com a primeira página de livros, usando o tamanho de página padrão

#### Scenario: Inactive books are excluded from the list
- **GIVEN** um livro ativo e um livro desativado (`is_active == false`)
- **WHEN** um cliente faz `GET /books`
- **THEN** a resposta contém o livro ativo e não contém o livro desativado

#### Scenario: Second read within the TTL is served from cache
- **GIVEN** uma primeira chamada a `GET /books` com uma combinação de filtros e paginação
- **WHEN** um segundo cliente faz `GET /books` com a mesma combinação de filtros e paginação, dentro da validade do cache
- **THEN** a resposta é 200 com o mesmo conteúdo, sem que a segunda chamada precise consultar o PostgreSQL

#### Scenario: Different filter combinations are cached independently
- **GIVEN** uma resposta de `GET /books?title=Clean` já em cache
- **WHEN** um cliente faz `GET /books?author=Martin`, uma combinação de filtros diferente
- **THEN** a resposta reflete o resultado correto para esse filtro, sem reaproveitar a entrada de cache de `title=Clean`

#### Scenario: Cache is invalidated after a book is created
- **GIVEN** uma resposta de `GET /books` já em cache, para qualquer combinação de filtros
- **WHEN** um novo livro é criado com sucesso via `POST /books`
- **THEN** a próxima chamada a `GET /books`, para qualquer combinação de filtros, reflete o novo livro, sem depender do TTL do cache expirar

#### Scenario: Cache is invalidated after a loan is created, returned or cancelled
- **GIVEN** uma resposta de `GET /books` já em cache
- **WHEN** um empréstimo referente a um livro dessa listagem é criado, devolvido ou cancelado com sucesso
- **THEN** a próxima chamada a `GET /books`, para qualquer combinação de filtros, reflete `available_copies` atualizado, sem depender do TTL do cache expirar

#### Scenario: Redis unavailable degrades to direct database read
- **GIVEN** o Redis está indisponível
- **WHEN** um cliente faz `GET /books`
- **THEN** a resposta ainda é 200 com os dados vindos do banco, sem erro para o cliente

#### Scenario: Redis unavailable during invalidation still lets the write succeed
- **GIVEN** o Redis está indisponível
- **WHEN** uma operação que normalmente invalidaria o cache de listagem (criação de livro, alteração, desativação, ou criação/devolução/cancelamento de empréstimo) é concluída com sucesso
- **THEN** a resposta da operação é a mesma que teria sem falha do Redis, sem erro para o cliente

### Requirement: Get book by id
`GET /books/{id}` SHALL retornar os detalhes de um livro ou 404 se não existir.

#### Scenario: Existing book is returned
- **GIVEN** um livro existente
- **WHEN** um cliente faz `GET /books/{id}` com o id desse livro
- **THEN** a resposta é 200 com os dados do livro

#### Scenario: Inactive book is still returned
- **GIVEN** um livro desativado (`is_active == false`)
- **WHEN** um cliente faz `GET /books/{id}` com o id desse livro
- **THEN** a resposta é 200 com `isActive: false`, ao contrário de `GET /books`, que não lista livros inativos

#### Scenario: Nonexistent book returns 404
- **GIVEN** nenhum livro com o id informado
- **WHEN** um cliente faz `GET /books/{id}`
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`

### Requirement: Get book availability
`GET /books/{id}/availability` SHALL retornar exemplares totais, disponíveis e o indicador de ativo, com leitura passando por cache Redis. Um valor divergente gravado no cache não influencia a criação de empréstimo: a decisão de disponibilidade para `POST /loans` vem sempre do PostgreSQL, nunca da leitura cacheada por este endpoint.

#### Scenario: Existing book returns availability
- **GIVEN** um livro existente com `total_copies` e `available_copies`
- **WHEN** um cliente faz `GET /books/{id}/availability`
- **THEN** a resposta é 200 com `totalCopies`, `availableCopies` e `isActive`

#### Scenario: Inactive book still returns availability
- **GIVEN** um livro desativado (`is_active == false`)
- **WHEN** um cliente faz `GET /books/{id}/availability`
- **THEN** a resposta é 200 com `isActive: false` e os exemplares mantidos

#### Scenario: Nonexistent book returns 404
- **GIVEN** nenhum livro com o id informado
- **WHEN** um cliente faz `GET /books/{id}/availability`
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`

#### Scenario: Second read within the TTL is served from cache
- **GIVEN** uma primeira chamada a `GET /books/{id}/availability`
- **WHEN** um segundo cliente faz `GET /books/{id}/availability` para o mesmo livro, dentro da validade do cache
- **THEN** a resposta é 200 com o mesmo conteúdo, sem que a segunda chamada precise consultar o PostgreSQL

#### Scenario: Read after a loan is created, returned or cancelled reflects the new availability
- **GIVEN** uma resposta de `GET /books/{id}/availability` já em cache
- **WHEN** um empréstimo desse livro é criado, devolvido ou cancelado com sucesso
- **THEN** a próxima chamada a `GET /books/{id}/availability` reflete `available_copies` atualizado, sem depender do TTL do cache expirar

#### Scenario: Read after book creation, update or deactivation reflects the new state
- **GIVEN** uma resposta de `GET /books/{id}/availability` já em cache
- **WHEN** o livro é alterado via `PATCH /books/{id}` ou desativado via `DELETE /books/{id}` com sucesso
- **THEN** a próxima chamada a `GET /books/{id}/availability` reflete o estado novo, sem depender do TTL do cache expirar

#### Scenario: A poisoned cache value never overrides the database for a loan decision
- **GIVEN** um valor de disponibilidade maior que a disponibilidade real gravado manualmente no Redis, na chave de disponibilidade de um livro com `available_copies == 0`
- **WHEN** um cliente faz `POST /loans` para esse livro
- **THEN** a resposta é 409 `problem+json` com `type: no-copy-available`, e nenhum empréstimo é criado — a rejeição vem do PostgreSQL, independentemente do valor divergente em cache

#### Scenario: Redis unavailable degrades to direct database read
- **GIVEN** o Redis está indisponível
- **WHEN** um cliente faz `GET /books/{id}/availability`
- **THEN** a resposta ainda é 200 com os dados vindos do banco, sem erro para o cliente

#### Scenario: Redis unavailable during invalidation still lets the write succeed
- **GIVEN** o Redis está indisponível
- **WHEN** uma operação que normalmente invalidaria a disponibilidade cacheada de um livro (criação, alteração, desativação, ou criação/devolução/cancelamento de empréstimo) é concluída com sucesso
- **THEN** a resposta da operação é a mesma que teria sem falha do Redis, sem erro para o cliente

### Requirement: Create book
`POST /books` SHALL validar o corpo da requisição, aplicar as invariantes de `Book` e rejeitar ISBN duplicado. A criação bem-sucedida grava um evento de auditoria `BookCreated` na mesma transação da inserção, com o payload no formato padronizado de criação: `{ "after": { ... } }`, contendo o estado inicial do livro.

#### Scenario: Valid body creates a book
- **GIVEN** um corpo válido com título, ISBN, autor e total de exemplares maior que zero
- **WHEN** um cliente faz `POST /books`
- **THEN** a resposta é 201 com o livro criado, ativo, `availableCopies == totalCopies`

#### Scenario: Invalid body is rejected
- **GIVEN** um corpo sem título, ou com total de exemplares zero ou negativo
- **WHEN** um cliente faz `POST /books`
- **THEN** a resposta é 400 `problem+json` com `type: validation-failed`

#### Scenario: Duplicate ISBN is rejected
- **GIVEN** um livro existente com um ISBN já normalizado
- **WHEN** um cliente faz `POST /books` com um ISBN que normaliza para o mesmo valor
- **THEN** a resposta é 409 `problem+json` com `type: book-isbn-duplicate`

#### Scenario: Successful creation is audited
- **GIVEN** um corpo válido de `POST /books`
- **WHEN** o livro é criado com sucesso
- **THEN** um evento `BookCreated` é gravado na mesma transação, com o id do livro, o ator (header `X-Actor`, ou `"anonymous"` se ausente), o timestamp UTC, o identificador de correlação da requisição, e o payload no formato `{ "after": { ... } }` contendo o estado inicial do livro

#### Scenario: Rejected creation does not leave an orphan event
- **GIVEN** um corpo que resulta em ISBN duplicado ou falha de validação
- **WHEN** a requisição `POST /books` é rejeitada
- **THEN** nenhum livro é criado e nenhum evento `BookCreated` é gravado

### Requirement: Update book
`PATCH /books/{id}` SHALL permitir alterar título, autor e total de exemplares de um livro ativo, sem deixar `available_copies` negativo. A atualização bem-sucedida grava um evento de auditoria `BookUpdated` na mesma transação, com o payload contendo apenas os campos que mudaram, incluindo os valores anterior e posterior de `totalCopies` e `availableCopies` quando a quantidade de exemplares muda — obtidos do estado já carregado em memória pelo handler antes da alteração, sem consulta adicional ao banco e sem janela de corrida.

#### Scenario: Valid update succeeds
- **GIVEN** um livro ativo existente
- **WHEN** um cliente faz `PATCH /books/{id}` alterando título, autor ou total de exemplares para um valor compatível com os exemplares já em circulação
- **THEN** a resposta é 200 com os dados atualizados

#### Scenario: Reducing total copies below what is checked out is rejected
- **GIVEN** um livro cujo `total_copies - available_copies` exemplares estão em circulação
- **WHEN** um cliente faz `PATCH /books/{id}` reduzindo `total_copies` para um valor menor que os exemplares em circulação
- **THEN** a resposta é 409 `problem+json` com `type: insufficient-available-copies`

#### Scenario: Updating an inactive book is rejected
- **GIVEN** um livro desativado (`is_active == false`)
- **WHEN** um cliente faz `PATCH /books/{id}`
- **THEN** a resposta é 409 `problem+json` com `type: book-inactive`

#### Scenario: Nonexistent book returns 404
- **GIVEN** nenhum livro com o id informado
- **WHEN** um cliente faz `PATCH /books/{id}`
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`

#### Scenario: Successful update is audited with before/after of changed fields
- **GIVEN** um livro ativo existente com `totalCopies` e `availableCopies` conhecidos
- **WHEN** um cliente faz `PATCH /books/{id}` alterando o total de exemplares com sucesso
- **THEN** um evento `BookUpdated` é gravado na mesma transação, com o payload contendo os valores anterior e posterior de `totalCopies` e `availableCopies`, além de qualquer outro campo alterado (título, autor)

#### Scenario: Changing only the title audits before/after of the title alone
- **GIVEN** um livro ativo existente com título, autor e total de exemplares conhecidos
- **WHEN** um cliente faz `PATCH /books/{id}` alterando apenas o título, mantendo autor e total de exemplares inalterados
- **THEN** um evento `BookUpdated` é gravado com o payload contendo `before`/`after` apenas da chave `title`, sem entradas para `author`, `totalCopies` ou `availableCopies`

#### Scenario: Update with no effective field change is still audited
- **GIVEN** um livro ativo existente
- **WHEN** um cliente faz `PATCH /books/{id}` reenviando exatamente os mesmos valores de título, autor e total de exemplares já vigentes
- **THEN** a resposta é 200, um evento `BookUpdated` é gravado (a operação foi processada com sucesso), e o payload não contém entradas de antes/depois para os campos que não mudaram

#### Scenario: Rejected update does not leave an orphan event
- **GIVEN** um livro inativo, inexistente, ou uma redução de exemplares insuficiente
- **WHEN** a requisição `PATCH /books/{id}` é rejeitada
- **THEN** nenhum evento `BookUpdated` é gravado

### Requirement: Deactivate book
`DELETE /books/{id}` SHALL desativar o livro (`is_active = false`) sem apagá-lo fisicamente, de forma idempotente. A desativação efetiva (de um livro que estava ativo) grava um evento de auditoria `BookDeactivated` na mesma transação; repetir a desativação de um livro já inativo não grava um novo evento.

#### Scenario: Deactivating an active book succeeds
- **GIVEN** um livro ativo
- **WHEN** um cliente faz `DELETE /books/{id}`
- **THEN** a resposta é 204 e o livro passa a `is_active == false`, preservando os demais campos

#### Scenario: Deactivating an already inactive book is a no-op
- **GIVEN** um livro já desativado
- **WHEN** um cliente faz `DELETE /books/{id}`
- **THEN** a resposta é 204, sem erro, e o estado do livro não muda

#### Scenario: Nonexistent book returns 404
- **GIVEN** nenhum livro com o id informado
- **WHEN** um cliente faz `DELETE /books/{id}`
- **THEN** a resposta é 404 `problem+json` com `type: book-not-found`

#### Scenario: Effective deactivation is audited
- **GIVEN** um livro ativo
- **WHEN** um cliente faz `DELETE /books/{id}` com sucesso
- **THEN** um evento `BookDeactivated` é gravado na mesma transação, com o payload `{ "before": { "isActive": true }, "after": { "isActive": false } }`

#### Scenario: Deactivating an already inactive book does not duplicate the event
- **GIVEN** um livro já desativado, sem evento `BookDeactivated` pendente de gravação
- **WHEN** um cliente faz `DELETE /books/{id}` novamente
- **THEN** a resposta é 204 e nenhum novo evento `BookDeactivated` é gravado

### Requirement: Correlation id is echoed on every response
Toda resposta de qualquer endpoint de `/books` SHALL incluir o header `X-Correlation-Id` — ecoado quando o cliente o envia, gerado pelo servidor quando ausente.

#### Scenario: Correlation id from the client is echoed back
- **GIVEN** um cliente envia o header `X-Correlation-Id`
- **WHEN** uma requisição a qualquer endpoint de `/books` é respondida, com sucesso ou erro
- **THEN** o header de resposta `X-Correlation-Id` é igual ao valor enviado pelo cliente

#### Scenario: Correlation id is generated when absent
- **GIVEN** um cliente não envia `X-Correlation-Id`
- **WHEN** uma requisição a qualquer endpoint de `/books` é respondida, com sucesso ou erro
- **THEN** o header de resposta `X-Correlation-Id` contém um valor gerado pelo servidor

### Requirement: Error responses are Problem Details with correlation and trace identifiers
Toda resposta de erro dos endpoints de catálogo SHALL ser `application/problem+json` com as extensões `correlationId` e `traceId`.

#### Scenario: Error body carries the client-provided correlation id
- **GIVEN** um cliente envia o header `X-Correlation-Id`
- **WHEN** uma requisição a qualquer endpoint de `/books` resulta em erro
- **THEN** o corpo Problem Details da resposta inclui a extensão `correlationId` igual ao header enviado, além do header de resposta

#### Scenario: Error body carries a server-generated correlation id
- **GIVEN** um cliente não envia `X-Correlation-Id`
- **WHEN** uma requisição a qualquer endpoint de `/books` resulta em erro
- **THEN** o corpo Problem Details da resposta inclui a extensão `correlationId` gerada pelo servidor
