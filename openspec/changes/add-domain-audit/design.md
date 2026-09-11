## Context

`AuditEvent` (`Domain/Audit/AuditEvent.cs`) e a tabela `audit_events` já existem, com `LoanCreated`, `LoanReturned` e `LoanCancelled` gravados por `Features/Loans/{CreateLoan,ReturnLoan,CancelLoan}.cs` na mesma `SaveChangesAsync` (ou transação) do fato. `CorrelationIdMiddleware` já resolve o identificador de correlação uma única vez por requisição e o alimenta em quatro lugares: (1) grava em `HttpContext.Items`, de onde os handlers de empréstimo já o leem para `AuditEvent.CorrelationId`; (2) grava no header de resposta `X-Correlation-Id`; (3) abre um `logger.BeginScope` com o valor, colocando-o no escopo de log de toda a requisição; (4) `ProblemDetailsExtensions` lê o mesmo `HttpContext.Items` para popular a extensão `correlationId` do Problem Details em caso de erro. `Features/Books/{CreateBook,UpdateBook,DeactivateBook}.cs` hoje não gravam nenhum evento. `Book` é sempre lido com `SingleOrDefaultAsync` (um `SELECT` comum, já existente para checar `is_active`/exemplares) e mutado em memória antes de `SaveChangesAsync` gerar um `UPDATE` de todas as colunas mapeadas — nenhum handler de catálogo usa `ExecuteUpdateAsync` condicional, diferente do decremento de `available_copies` em `CreateLoan`. Ver proposal.md para o porquê.

## Goals / Non-Goals

**Goals:**
- Fechar a trilha de auditoria para os três eventos de livro, na mesma transação da alteração, sem `SELECT` extra para obter o estado anterior.
- Expor a trilha via `GET /audit-events`, com paginação determinística mesmo com eventos no mesmo instante.
- Formalizar a correlação como requisito cross-cutting testado, não apenas comportamento incidental de `/loans`.

**Non-Goals:**
- Logs estruturados, métricas ou traces (ficam para uma change de observabilidade).
- Expurgo ou retenção de eventos antigos.
- Autenticação real por trás de `X-Actor`.
- Qualquer mudança de contrato em `POST /loans`, que já grava `correlation_id` corretamente.

## Decisions

### O estado anterior vem do `SELECT` que o handler já fazia, não de `RETURNING`
Correção em relação a uma versão anterior deste documento: o estado "antes" **não** vem de `RETURNING`. `CreateBook` não tem "antes" (é criação). `UpdateBook` e `DeactivateBook` já faziam `SingleOrDefaultAsync` — um `SELECT` comum — para checar `is_active` e os exemplares em circulação, antes desta change existir; o payload de auditoria reaproveita esse mesmo objeto em memória como "antes", e o estado da entidade após `UpdateDetails`/`Deactivate` como "depois". O `SaveChangesAsync` gera um `UPDATE` comum (via change tracking do EF), não um `ExecuteUpdateAsync` condicional — não há `WHERE` sobre os valores lidos, e nenhuma verificação de concorrência otimista (proibida pelo CLAUDE.md: sem `rowversion`).

`RETURNING` foi descartado porque não há nada a que aplicá-lo: a técnica do empréstimo existe para extrair o "antes" de um `ExecuteUpdateAsync` que é condicional e não traz a entidade de volta ao `DbContext`. Aqui a entidade já está em memória porque o handler leu `SingleOrDefaultAsync`.

Risco aceito e não introduzido por esta change: como o `SELECT` e o `UPDATE` não formam uma unidade atômica (sem `SELECT ... FOR UPDATE`, sem token de concorrência), dois `PATCH /books/{id}` concorrentes para o mesmo livro leem o mesmo estado "antes" e o `SaveChangesAsync` que commitar por último sobrescreve os campos do outro (lost update) — o evento `BookUpdated` da requisição perdedora descreveria uma mudança que o banco não reflete mais. Este risco já existe hoje em `UpdateBook.cs`, independente de auditoria; esta change não o resolve porque a seção Concorrência do CLAUDE.md exige `UPDATE` atômico condicional apenas para o decremento de `available_copies` do empréstimo, não para o `PATCH` administrativo de livro. Ver Risks/Trade-offs.

### Formato do payload: apenas os campos relevantes, nunca a entidade inteira
Conforme CLAUDE.md, o payload é o formato padronizado do projeto, não `JsonSerializer.Serialize(book)`. Exemplos concretos:

`BookCreated` (criação, `{ "after": { ... } }`):
```json
{ "after": { "title": "Clean Code", "isbn": "9780132350884", "author": "Robert C. Martin", "totalCopies": 3, "availableCopies": 3 } }
```

`BookUpdated` alterando só o título (`{ "before": ..., "after": ... }`, apenas os campos que mudaram):
```json
{ "before": { "title": "Clean Cod" }, "after": { "title": "Clean Code" } }
```

`BookUpdated` alterando a quantidade de exemplares (`totalCopies` e `availableCopies` sempre juntos, porque um deriva do outro pelo mesmo delta):
```json
{ "before": { "totalCopies": 3, "availableCopies": 1 }, "after": { "totalCopies": 5, "availableCopies": 3 } }
```

`BookDeactivated` (sempre o mesmo par fixo, já que só há uma transição possível):
```json
{ "before": { "isActive": true }, "after": { "isActive": false } }
```

Nunca a entidade inteira (por exemplo, incluir `id`, `createdAtUtc` ou campos que não mudaram no payload de `BookUpdated`) — isso contradiz "apenas os campos relevantes" e infla o `jsonb` sem necessidade.

### `AuditEvent.Id` migra de `Guid` para `bigint` identity
Motivo: `GET /audit-events` precisa de um critério de desempate estável para paginar eventos com o mesmo `occurred_at_utc` (cenário explícito na spec). Um `bigint` gerado pelo banco em ordem de inserção serve como desempate monotônico sem exigir uma coluna adicional (`sequence`/`row_number`) nem comparação lexicográfica de `Guid`, que não reflete ordem de criação. A resposta HTTP continua expondo o id como string, então o tipo interno não é um contrato quebrado para o cliente.
Alternativas descartadas:
- **Manter `Guid` e desempatar por `Id` (lexicográfico)**: não relacionado à ordem de inserção; dois clientes não teriam como prever a ordem de desempate a partir do domínio (ela seria arbitrária, ainda que estável).
- **Coluna `sequence`/`row_number` adicional**: duplica o que uma PK `bigint identity` já oferece.

### Índices `(entity_type, entity_id, occurred_at_utc)` e `(correlation_id)`
Cobrem os dois padrões de filtro mais prováveis da consulta: "histórico de uma entidade" e "todos os eventos de uma requisição". Sem eles, `GET /audit-events` faria `seq scan` completo na tabela conforme ela cresce — a tabela é append-only e só cresce.

### `BookUpdated` sempre grava, mesmo sem mudança efetiva de campo
Decisão: gravar o evento sempre que `PATCH /books/{id}` é aceito (200), mesmo que os valores enviados sejam idênticos aos vigentes — a operação foi processada com sucesso e o handler não distingue "PATCH com os mesmos valores" de "PATCH que efetivamente muda algo" antes de decidir se grava o evento. O payload, porém, só lista campos com `before != after`; se nada mudou, `before`/`after` ficam sem entradas. Alternativa descartada: pular a gravação do evento quando nenhum campo muda — rejeitada porque exigiria uma verificação extra de igualdade componente a componente cujo único efeito seria uma trilha *menos* completa, sem benefício correspondente (o requisito de "nunca existir evento sem a alteração correspondente" se refere a criação, onde a alternativa seria pior — livro sem nenhum evento).

### Sobreposição consciente com o requisito de correlação de `catalog-endpoints`
`catalog-endpoints` já fixa "Correlation id is echoed on every response" e "Error responses are Problem Details..." com escopo restrito a `/books`. Esta change não move nem remove esse requisito — apenas acrescenta, em `audit-trail`, a versão cross-cutting para toda a API. As duas descrições passam a coexistir e descrevem o mesmo comportamento observável em `/books`. Aceito por ora: mover o requisito de `catalog-endpoints` para apenas referenciar o de `audit-trail` é uma limpeza de spec sem mudança de comportamento, fora do escopo desta change (viraria refatoração oportunista). Fica registrado aqui para uma change futura de consolidação, se a duplicação incomodar na manutenção.

### Sem interceptor de `SaveChanges`, sem trigger de banco, sem outbox
Conforme CLAUDE.md (seção Auditoria): um interceptor de `SaveChanges` não observaria futuras chamadas a `ExecuteUpdateAsync` (usadas no empréstimo), então adotar o padrão para o catálogo criaria duas formas de gravar auditoria — uma explícita, uma por interceptor — inconsistentes entre si. Descartado.
Trigger no PostgreSQL: moveria a regra de negócio (o que entra no payload, quais campos importam) para dentro do banco, tornando-a invisível para quem lê o código da aplicação e impossível de testar com xUnit sem um banco real rodando o trigger. Descartado.
Outbox com consumidor: resolve entrega de eventos para *sistemas externos* de forma assíncrona; aqui a auditoria é interna, consultada pela própria API, e precisa estar visível na mesma transação do fato (para os cenários "evento sem a alteração correspondente" e "alteração sem evento" serem impossíveis por construção). Um outbox introduziria uma janela onde a alteração existe e o evento ainda não foi publicado — o oposto do requisito. Descartado.

## Risks / Trade-offs

- [Migrar `AuditEvent.Id` de `Guid` para `bigint`] → é uma mudança de schema numa tabela que já tem linhas (dos eventos de empréstimo criados nas changes anteriores); a migration precisa recriar a PK. Mitigação: ambiente ainda não tem dados de produção reais (desafio técnico), e a migration roda via `MigrateAsync` (dev) ou pelo `migrator` (demais ambientes) antes de qualquer tráfego.
- [Índice `(correlation_id)` sem tipo `uuid`, pois `correlation_id` é uma string livre vinda do header `X-Correlation-Id`] → índice B-tree padrão em `text`/`varchar` é suficiente; não há necessidade de índice especializado.
- [`BookUpdated` sempre gravado, mesmo sem mudança] → aumenta o volume de eventos para clientes que fazem `PATCH` "no-op" repetidamente. Aceito: é o mesmo comportamento observável de qualquer chamada bem-sucedida à API, e o payload deixa claro que nada mudou.
- [Lost update entre dois `PATCH /books/{id}` concorrentes para o mesmo livro] → o evento `BookUpdated` da requisição sobrescrita continua gravado na trilha mesmo depois de o banco não refletir mais aquela mudança, porque o `SELECT`+`UPDATE` do handler não é atômico e não há verificação de concorrência. Mitigação: nenhuma nesta change — pré-existente em `UpdateBook.cs`, fora do escopo definido em `tasks.md` (CLAUDE.md só exige `UPDATE` atômico condicional para o decremento de `available_copies` do empréstimo). Registrado para uma change futura que queira estender a garantia de concorrência do empréstimo ao `PATCH` de livro, caso o risco deixe de ser aceitável.

## Migration Plan

1. Nova migration adicionando os dois índices e alterando `audit_events.id` para `bigint` gerado pelo banco (identity), recriando a PK.
2. Nenhuma mudança de contrato para consumidores existentes de `POST /loans` ou dos endpoints de `/books` além da nova gravação de evento (não observável externamente, exceto via `GET /audit-events`, que é novo).
3. Sem plano de rollback de dados: a tabela é append-only e o desafio não tem ambiente de produção com dados reais a preservar.
