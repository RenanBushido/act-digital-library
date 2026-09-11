## MODIFIED Requirements

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
