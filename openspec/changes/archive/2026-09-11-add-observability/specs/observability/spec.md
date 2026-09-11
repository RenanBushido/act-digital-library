## Purpose

Torna a aplicação operável sob múltiplas réplicas: health checks separados por finalidade para o orquestrador decidir roteamento, métricas de negócio para os empréstimos, logging estruturado consumível por um coletor e traces que correlacionam requisição HTTP, banco e cache.

## ADDED Requirements

### Requirement: Liveness health check
`GET /health/live` SHALL indicar apenas que o processo está ativo, sem consultar PostgreSQL nem Redis. O endpoint SHALL responder 200 mesmo com essas dependências indisponíveis.

#### Scenario: Liveness com PostgreSQL indisponível
- **WHEN** o PostgreSQL está fora do ar e o cliente chama `GET /health/live`
- **THEN** a resposta é 200

#### Scenario: Liveness com PostgreSQL e Redis indisponíveis
- **WHEN** tanto o PostgreSQL quanto o Redis estão fora do ar e o cliente chama `GET /health/live`
- **THEN** a resposta é 200

### Requirement: Readiness health check
`GET /health/ready` SHALL consultar PostgreSQL e Redis. Indisponibilidade do PostgreSQL SHALL tornar a aplicação não pronta, respondendo 503. Indisponibilidade do Redis, com PostgreSQL saudável, SHALL manter a aplicação pronta, respondendo 200 e identificando o Redis como degradado. O corpo da resposta SHALL identificar o estado de cada dependência verificada.

#### Scenario: Readiness com PostgreSQL indisponível
- **WHEN** o PostgreSQL está fora do ar e o cliente chama `GET /health/ready`
- **THEN** a resposta é 503
- **AND** o corpo identifica o PostgreSQL como não saudável

#### Scenario: Readiness com apenas o Redis indisponível
- **WHEN** o PostgreSQL está saudável, o Redis está fora do ar, e o cliente chama `GET /health/ready`
- **THEN** a resposta é 200
- **AND** o corpo identifica o Redis como degradado e o PostgreSQL como saudável

### Requirement: Métricas de empréstimo
O `Meter` `Library.Loans` SHALL expor: um contador `library.loans.created` incrementado a cada empréstimo criado com sucesso; um contador `library.loans.rejected` com a tag `reason` (`unavailable`, `book_inactive`, `book_not_found` ou `user_not_found`) incrementado a cada rejeição de regra de negócio em `POST /loans`; um contador `library.loans.idempotent_replays` incrementado a cada repetição idempotente de `POST /loans`; e um histograma `library.loans.create.duration`, em milissegundos, com a tag `outcome` (`created`, `replayed` ou `rejected`), medindo a duração do endpoint inteiro, incluindo o filtro de idempotência. Uma réplica idempotente SHALL incrementar `idempotent_replays` e SHALL NOT incrementar `rejected`.

`library.loans.rejected` SHALL contar exclusivamente as quatro razões de negócio listadas acima. As respostas do filtro de idempotência que não são negócio nem replay bem-sucedido — `Idempotency-Key` ausente (400), chave em voo (409 `request-in-flight`) e reuso de chave com corpo diferente (422 `idempotency-key-reuse`) — SHALL NOT incrementar `library.loans.rejected` nem `library.loans.idempotent_replays`. O histograma `library.loans.create.duration` SHALL ainda assim registrar uma observação para essas três respostas, com `outcome=rejected`, por serem, do ponto de vista de latência do endpoint, requisições que não completaram a criação nem devolveram uma resposta já concluída.

#### Scenario: Empréstimo criado com sucesso
- **WHEN** `POST /loans` cria um empréstimo com sucesso
- **THEN** `library.loans.created` é incrementado em 1
- **AND** `library.loans.create.duration` registra uma observação com `outcome=created`

#### Scenario: Rejeição por indisponibilidade de exemplar
- **WHEN** `POST /loans` é rejeitado porque o livro não tem exemplar disponível
- **THEN** `library.loans.rejected` é incrementado em 1 com a tag `reason=unavailable`
- **AND** `library.loans.create.duration` registra uma observação com `outcome=rejected`

#### Scenario: Rejeição por livro inativo, inexistente ou usuário inexistente
- **WHEN** `POST /loans` é rejeitado porque o livro está inativo, o livro não existe, ou o usuário não existe
- **THEN** `library.loans.rejected` é incrementado em 1 com a tag `reason` igual a, respectivamente, `book_inactive`, `book_not_found` ou `user_not_found`

#### Scenario: Repetição idempotente não conta como rejeição
- **WHEN** `POST /loans` é repetido com a mesma `Idempotency-Key` e o mesmo corpo de uma requisição já concluída
- **THEN** `library.loans.idempotent_replays` é incrementado em 1
- **AND** `library.loans.rejected` não é incrementado
- **AND** `library.loans.create.duration` registra uma observação com `outcome=replayed`

#### Scenario: Resposta de idempotência sem reason de negócio não conta como rejected nem replayed
- **WHEN** `POST /loans` é recusado pelo filtro de idempotência sem chegar a uma decisão de negócio — `Idempotency-Key` ausente, chave em voo, ou reuso de chave com corpo diferente
- **THEN** nem `library.loans.rejected` nem `library.loans.idempotent_replays` são incrementados
- **AND** `library.loans.create.duration` registra uma observação com `outcome=rejected`

### Requirement: Logs estruturados de empréstimo
As operações de criação, devolução e cancelamento de empréstimo SHALL produzir log com identificadores de negócio (`LoanId`, `BookId`, `UserId`, `CorrelationId`, e `AvailableAfter` quando aplicável) como propriedades estruturadas, nunca interpoladas na mensagem. A saída SHALL ser JSON, consumível por um coletor sem parser próprio.

#### Scenario: Empréstimo criado com sucesso gera log estruturado
- **WHEN** `POST /loans` cria um empréstimo com sucesso
- **THEN** um log é emitido com `LoanId`, `BookId`, `UserId` e `CorrelationId` como propriedades estruturadas, não interpoladas na mensagem

### Requirement: Traces distribuídos
Requisições HTTP, consultas ao PostgreSQL e operações no Redis SHALL aparecer no trace, correlacionadas entre si. Os endpoints `/health/live` e `/health/ready` SHALL NOT gerar traces. O destino do exportador OTLP SHALL vir de configuração, nunca fixo no código.

#### Scenario: Endpoints de health não geram trace
- **WHEN** o cliente chama `GET /health/live` ou `GET /health/ready`
- **THEN** nenhum trace é exportado para essa requisição

#### Scenario: Requisição de empréstimo gera trace correlacionado
- **WHEN** `POST /loans` é processado, envolvendo consulta ao PostgreSQL
- **THEN** o trace da requisição HTTP inclui os spans do PostgreSQL sob a mesma correlação
