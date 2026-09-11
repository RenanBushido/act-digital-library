## Purpose

Define a consulta somente leitura da trilha de auditoria (`GET /audit-events`) e o requisito de correlação que amarra toda requisição, em qualquer endpoint da API, ao evento de auditoria que ela produz.

## ADDED Requirements

### Requirement: Query audit trail
`GET /audit-events` SHALL retornar uma página de eventos de auditoria, com filtros opcionais por tipo de entidade, identificador de entidade, ação, ator, identificador de correlação e intervalo de datas (`occurred_at_utc`). Nenhum filtro é obrigatório; combiná-los restringe a interseção dos resultados.

#### Scenario: List returns paginated audit events
- **GIVEN** eventos de auditoria existentes
- **WHEN** um cliente faz `GET /audit-events` sem parâmetros
- **THEN** a resposta é 200 com a primeira página de eventos, ordenada do mais recente para o mais antigo

#### Scenario: Filter by entity type and entity id
- **GIVEN** eventos de auditoria de diferentes tipos e identificadores de entidade
- **WHEN** um cliente faz `GET /audit-events` com `entityType` e `entityId`
- **THEN** a resposta contém apenas os eventos daquela entidade específica

#### Scenario: Filter by action
- **GIVEN** eventos com ações diferentes (por exemplo `BookCreated` e `LoanCreated`)
- **WHEN** um cliente faz `GET /audit-events` com `action=BookCreated`
- **THEN** a resposta contém apenas eventos com essa ação

#### Scenario: Filter by actor
- **GIVEN** eventos gravados por atores diferentes
- **WHEN** um cliente faz `GET /audit-events` com `actor` igual a um dos valores existentes
- **THEN** a resposta contém apenas eventos daquele ator

#### Scenario: Filter by correlation id
- **GIVEN** dois ou mais eventos gravados pela mesma requisição, compartilhando o mesmo identificador de correlação
- **WHEN** um cliente faz `GET /audit-events` com esse `correlationId`
- **THEN** a resposta contém exatamente os eventos daquela requisição, e nenhum de outra

#### Scenario: Filter by date range
- **GIVEN** eventos com `occurred_at_utc` em datas distintas
- **WHEN** um cliente faz `GET /audit-events` com um intervalo de datas que cobre apenas parte deles
- **THEN** a resposta contém apenas os eventos cujo `occurred_at_utc` está dentro do intervalo

#### Scenario: Filter combination matching nothing returns an empty page
- **GIVEN** eventos de auditoria existentes
- **WHEN** um cliente faz `GET /audit-events` com uma combinação de filtros que nenhum evento satisfaz
- **THEN** a resposta é 200 com uma página vazia, não um erro

### Requirement: Audit trail pagination is deterministic
A paginação de `GET /audit-events` SHALL ter ordenação determinística e estável entre páginas, incluindo um critério de desempate para eventos com o mesmo `occurred_at_utc`.

#### Scenario: Two events at the same instant are ordered deterministically
- **GIVEN** dois eventos de auditoria gravados com o mesmo `occurred_at_utc`
- **WHEN** um cliente pagina os resultados de `GET /audit-events` (por exemplo, página 1 com tamanho 1, depois página 2)
- **THEN** cada evento aparece em exatamente uma página, em uma ordem consistente entre chamadas repetidas, sem duplicação nem omissão

### Requirement: Audit trail is read-only
Nenhum endpoint da API SHALL permitir alterar ou remover um evento de auditoria já gravado.

#### Scenario: No write endpoint exists for audit events
- **GIVEN** um evento de auditoria existente
- **WHEN** um cliente tenta qualquer operação de escrita sobre `audit-events` (não existe endpoint de criação, alteração ou remoção)
- **THEN** o evento permanece inalterado; a única forma de acessá-lo é via `GET /audit-events`

### Requirement: Correlation id propagates across the entire API
Toda requisição, em qualquer endpoint, SHALL ter um identificador de correlação: o do header `X-Correlation-Id` quando enviado pelo cliente, ou um gerado pelo servidor quando ausente. O mesmo identificador aparece no header de resposta, no escopo de log da requisição, no Problem Details em caso de erro, e em todo evento de auditoria gravado por essa requisição.

#### Scenario: Correlation id from the client reaches the audit event
- **GIVEN** um cliente envia `X-Correlation-Id` em uma requisição que grava um evento de auditoria (por exemplo, `POST /books` ou `POST /loans`)
- **WHEN** a requisição é processada com sucesso
- **THEN** o evento de auditoria gravado tem `correlation_id` igual ao valor enviado pelo cliente

#### Scenario: Correlation id is generated when absent and still reaches the audit event
- **GIVEN** um cliente não envia `X-Correlation-Id` em uma requisição que grava um evento de auditoria
- **WHEN** a requisição é processada com sucesso
- **THEN** o header de resposta `X-Correlation-Id` contém um valor gerado pelo servidor, e o evento de auditoria gravado tem esse mesmo valor em `correlation_id`

#### Scenario: Two operations of the same request share the correlation id
- **GIVEN** uma requisição cujo processamento envolve mais de uma escrita relacionada (por exemplo, o decremento de `available_copies` e a criação do `Loan`, ambos parte de `POST /loans`)
- **WHEN** a requisição é concluída com sucesso
- **THEN** todo evento de auditoria gerado por essa requisição carrega o mesmo identificador de correlação

#### Scenario: Correlation id is echoed even on a read-only endpoint that writes no audit event
- **GIVEN** um cliente não envia `X-Correlation-Id`
- **WHEN** o cliente faz `GET /audit-events` (endpoint somente leitura, que não grava nenhum evento de auditoria por si só)
- **THEN** o header de resposta `X-Correlation-Id` contém um valor gerado pelo servidor, confirmando que a propagação não depende de a requisição produzir um evento de auditoria
