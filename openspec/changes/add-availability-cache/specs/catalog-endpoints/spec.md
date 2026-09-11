## MODIFIED Requirements

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
