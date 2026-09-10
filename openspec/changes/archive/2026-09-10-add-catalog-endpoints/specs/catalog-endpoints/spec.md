## Purpose

Define o contrato HTTP de leitura e escrita do catálogo de livros (`Book`): as respostas, os erros e as garantias de cache que os endpoints `GET/POST/PATCH/DELETE /books` precisam cumprir.

## ADDED Requirements

### Requirement: List books
`GET /books` SHALL retornar uma lista paginada de livros ativos, com leitura passando por cache Redis.

#### Scenario: List returns paginated books
- **GIVEN** livros existentes no catálogo
- **WHEN** um cliente faz `GET /books` sem parâmetros
- **THEN** a resposta é 200 com a primeira página de livros, usando o tamanho de página padrão

#### Scenario: Inactive books are excluded from the list
- **GIVEN** um livro ativo e um livro desativado (`is_active == false`)
- **WHEN** um cliente faz `GET /books`
- **THEN** a resposta contém o livro ativo e não contém o livro desativado

#### Scenario: Cache is invalidated after a book is created
- **GIVEN** uma resposta de `GET /books` já em cache
- **WHEN** um novo livro é criado com sucesso via `POST /books`
- **THEN** a próxima chamada a `GET /books` reflete o novo livro, sem depender do TTL do cache expirar

#### Scenario: Redis unavailable degrades to direct database read
- **GIVEN** o Redis está indisponível
- **WHEN** um cliente faz `GET /books`
- **THEN** a resposta ainda é 200 com os dados vindos do banco, sem erro para o cliente

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
`GET /books/{id}/availability` SHALL retornar exemplares totais, disponíveis e o indicador de ativo, com leitura passando por cache Redis.

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

### Requirement: Create book
`POST /books` SHALL validar o corpo da requisição, aplicar as invariantes de `Book` e rejeitar ISBN duplicado.

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

### Requirement: Update book
`PATCH /books/{id}` SHALL permitir alterar título, autor e total de exemplares de um livro ativo, sem deixar `available_copies` negativo.

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

### Requirement: Deactivate book
`DELETE /books/{id}` SHALL desativar o livro (`is_active = false`) sem apagá-lo fisicamente, de forma idempotente.

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
