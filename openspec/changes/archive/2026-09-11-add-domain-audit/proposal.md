## Why

A tabela `audit_events` e os eventos de empréstimo (`LoanCreated`, `LoanReturned`, `LoanCancelled`) já existem desde a change `add-loan-concurrency`. A trilha de auditoria fica incompleta sem os eventos de catálogo (`BookCreated`, `BookUpdated`, `BookDeactivated`) e sem uma forma de consultá-la — hoje os eventos são gravados mas ninguém pode lê-los. Além disso, a propagação do identificador de correlação até o evento de auditoria e o Problem Details precisa ser um requisito formal e coberto por teste em todos os endpoints, não só nos de catálogo.

## What Changes

- `POST /books`, `PATCH /books/{id}` e `DELETE /books/{id}` passam a gravar `BookCreated`, `BookUpdated` e `BookDeactivated` na mesma transação da alteração, com payload padronizado (apenas campos que mudaram). **BREAKING**: falha de negócio ou de banco na escrita do livro não pode mais deixar o evento correspondente órfão — isso já era a garantia implícita da transação única, mas passa a ser um requisito testado.
- Payload de `BookUpdated` registra `totalCopies`/`availableCopies` antes e depois quando a quantidade de exemplares muda, obtidos do estado já carregado em memória pelo handler — sem `SELECT` extra e sem janela de corrida.
- Novo endpoint `GET /audit-events`, somente leitura, com filtros opcionais (tipo de entidade, id da entidade, ação, ator, id de correlação, intervalo de datas) e paginação com ordenação determinística (critério de desempate para eventos no mesmo instante).
- `AuditEvent` passa a ter identificador `bigint` gerado pelo banco (era `Guid`), usado como critério de desempate estável na paginação por `occurred_at_utc`; índices `(entity_type, entity_id, occurred_at_utc)` e `(correlation_id)` são adicionados ao schema.
- O middleware de correlação (já implementado e usado pelos empréstimos) passa a ser um requisito de spec explícito e testado para **todos** os endpoints da API, não apenas `/books`: o identificador do header `X-Correlation-Id` (ou gerado, se ausente) aparece no header de resposta, no escopo de log, no Problem Details de erro e em qualquer evento de auditoria gerado pela requisição.
- Nenhum endpoint permite alterar ou remover um evento de auditoria já gravado.

## Capabilities

### New Capabilities

- `audit-trail`: consulta paginada e filtrável da trilha de auditoria (`GET /audit-events`), imutabilidade dos eventos, e o requisito de correlação como propriedade cross-cutting de toda a API (não restrito a `/books`).

### Modified Capabilities

- `catalog-endpoints`: os requisitos "Create book", "Update book" e "Deactivate book" passam a exigir a gravação do evento de auditoria correspondente na mesma transação, com cenários negativos (falha de negócio não deixa evento órfão, alteração sem mudança efetiva de campo, desativação de livro já inativo não duplica evento).

## Impact

- Código: `Features/Books/CreateBook.cs`, `UpdateBook.cs`, `DeactivateBook.cs` passam a construir e persistir `AuditEvent`. Novo `Features/Audit/` com `GetAuditEvents.cs`, `AuditEventsEndpoints.cs` e `Contracts/`.
- Domínio: `Domain/Audit/AuditEvent.cs` troca a chave primária de `Guid` para `bigint` identity; `Infrastructure/Persistence/AuditEventConfiguration.cs` ganha os dois índices.
- Banco: nova migration em `src/Library.Api/Migrations` para o tipo da chave e os índices.
- Testes de integração cobrindo os cenários acima com Testcontainers.
- Sem mudança de contrato para `POST /loans` e seus eventos — já emitem `correlation_id`; a mudança de Id de `AuditEvent` é interna e não afeta o payload retornado ao cliente em `GET /audit-events`, que expõe o Id como string.
