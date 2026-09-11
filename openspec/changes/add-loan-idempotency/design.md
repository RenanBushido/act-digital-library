## Context

`add-loan-concurrency` já implementou `RequireIdempotencyKeyFilter` (`IEndpointFilter`, só em `POST /loans`) validando apenas a presença do header, e `CreateLoan.HandleAsync` já abre uma transação explícita (`BeginTransactionAsync` → `ExecuteUpdateAsync` condicional → insert `Loan` + `AuditEvent` → `SaveChangesAsync` → `CommitAsync`). O `CLAUDE.md`, seção Idempotência, já fixa o desenho completo desta change antes dela existir: schema da tabela `idempotency_keys`, base do `request_hash` (bytes crus do corpo + template da rota, via `Request.EnableBuffering()`), divisão de responsabilidade entre filtro e handler, e a tabela de erros (`idempotency-key-reuse` 422, `request-in-flight` 409). Este `design.md` decide como encaixar isso no filtro e no handler existentes, sem reabrir nenhuma dessas decisões.

## Goals / Non-Goals

**Goals:**
- Deduplicação real de `POST /loans` por `Idempotency-Key`: replay de sucesso, rejeição de reuso com corpo diferente, rejeição de requisição em voo, liberação da chave em rejeição de negócio/exceção.
- Gravação da resposta idempotente na mesma transação do empréstimo (nunca em transação separada, nunca só no filtro).
- Teste de concorrência com a mesma chave (N requisições, 1 chave), além do teste já existente com N chaves distintas.

**Non-Goals:**
- Expurgo automático de chaves expiradas — `expires_at_utc` fica gravado (janela de 24h) mas nenhum processo remove linhas; fica como limitação documentada (ver Risks).
- Idempotência em `POST /loans/{id}/return` e `POST /loans/{id}/cancel` — fora do escopo do brief (`docs/4 - add-loan-idempotency.md`).
- Cache Redis em qualquer leitura — nada nesta change toca `GET /books` ou `GET /books/{id}/availability`.
- Métricas OpenTelemetry (`library.loans.idempotent_replays` já está no catálogo de métricas do `CLAUDE.md`, mas emissão fica para a change de observabilidade, assim como as demais métricas de `Library.Loans` que ainda não foram implementadas).

## Decisions

### Alternativas descartadas (exigidas pelo brief)

- **Deduplicação no Redis com `SET NX`**: o `CLAUDE.md` proíbe `IMemoryCache`/locks para idempotência e fixa "fonte da verdade é o PostgreSQL". Redis não tem a durabilidade transacional necessária para garantir que a reserva da chave e a criação do empréstimo sejam atômicas entre si — um `SET NX` bem-sucedido seguido de falha ao criar o empréstimo deixaria a chave "reservada" no Redis sem nenhum jeito atômico de reverter junto com o rollback do Postgres. A tabela `idempotency_keys` no mesmo banco participa da mesma transação do empréstimo; o Redis não pode.
- **Chave natural de negócio com índice parcial** (ex.: unique index em `(book_id, user_id, data)` para inferir duplicidade): não captura o contrato do brief — o cliente pode legitimamente criar dois empréstimos do mesmo livro para o mesmo usuário no mesmo dia (`loan-management` já fixa esse cenário: "o mesmo usuário pode ter mais de um empréstimo ativo do mesmo livro"). Idempotência aqui é sobre a *requisição* (identificada pelo header), não sobre uma regra de negócio implícita nos dados do empréstimo.
- **Replay por reconsulta do empréstimo** (guardar só `resource_id` e, no replay, buscar o `Loan` e serializar de novo): o corpo da resposta original poderia divergir do estado atual do recurso (o empréstimo pode já ter sido devolvido entre a criação e o replay), violando "mesmo status, mesmo corpo" do requisito. Guardar `response_body` literal garante replay bit-a-bit da resposta original, independente do que aconteceu com o recurso depois.

### Cálculo do `request_hash`

SHA-256 sobre os bytes crus do corpo (via `Request.EnableBuffering()` + leitura do stream, nunca reserialização do DTO já desserializado) concatenados com o template da rota (`POST /loans`). Reserializar o DTO arriscaria produzir um hash diferente para o mesmo corpo lógico (ordem de propriedades, formatação de espaço em branco) — o `CLAUDE.md` já proíbe isso explicitamente. `Request.EnableBuffering()` é chamado no início do filtro, antes de qualquer leitura, para permitir que o model binder do Minimal API leia o corpo de novo depois.

### Divisão de responsabilidade: filtro reserva, handler grava

Reafirmando o `CLAUDE.md` (seção "Divisão de responsabilidade"):
- **`RequireIdempotencyKeyFilter`**: valida presença do header (já existe) → calcula `request_hash` → `INSERT INTO idempotency_keys (key, endpoint, request_hash, state, expires_at_utc) VALUES (..., 'InFlight', ...) ON CONFLICT DO NOTHING`. Se a linha não foi inserida (já existia), lê a linha existente: `request_hash` diferente → 422 `idempotency-key-reuse`; `state == Completed` → devolve `status_code`/`response_body` armazenados com `Idempotency-Replayed: true`; `state == InFlight` → 409 `request-in-flight`. Se a linha foi inserida (nova reserva), chama `next(context)`; se o handler lançar exceção ou devolver um resultado que não é 2xx, o filtro apaga a linha `InFlight` (libera a chave) antes de propagar a resposta.
- **`CreateLoan.HandleAsync`**: continua decidindo a regra de negócio (livro/usuário existem, exemplar disponível) como hoje. Só a parte de sucesso muda: dentro da mesma transação já aberta, depois de montar `LoanResponse`, serializa o DTO e roda um `ExecuteUpdateAsync` condicional sobre `idempotency_keys` — `Where(k => k.Key == key && k.Endpoint == endpoint).ExecuteUpdateAsync(setters => setters.SetProperty(k => k.State, IdempotencyState.Completed).SetProperty(k => k.StatusCode, 201).SetProperty(k => k.ResponseBody, body).SetProperty(k => k.ResourceId, loan.Id), ct)` — antes do `CommitAsync`, mesmo padrão já usado para o contador do livro (`ExecuteUpdateAsync` condicional em vez de carregar a entidade e rastrear). Uma rejeição de negócio (`affected == 0` no `ExecuteUpdateAsync` do livro, livro/usuário não encontrado) retorna o `Result` de erro normalmente — o filtro, ao ver um status não-2xx, libera a chave.

Esse desenho evita que o handler precise saber como o filtro decide reservar/liberar: o handler só grava a resposta em caso de sucesso; o filtro é o único lugar que decide o destino da linha `idempotency_keys` no caso de falha.

### A reserva da chave (`InFlight`) é commitada antes do handler começar — não faz parte da transação do empréstimo

O `INSERT ... ON CONFLICT DO NOTHING` do filtro roda como sua própria escrita (commit implícito do EF/Npgsql), **antes** de `next(context)` chamar o handler e **antes** de `BeginTransactionAsync` do handler existir. Isso é proposital, não um detalhe incidental: se a reserva fizesse parte da mesma transação do empréstimo, nenhuma segunda requisição concorrente conseguiria enxergar a linha `InFlight` enquanto a primeira ainda estivesse processando (READ COMMITTED só vê dados commitados) — toda segunda requisição colidiria em lock de escrita ou, pior, o `SELECT` de leitura da linha existente não veria o `InFlight`, quebrando a rejeição `request-in-flight`. Commitar a reserva de imediato é o que torna o estado `InFlight` observável por outras requisições enquanto a primeira ainda está em voo.

Consequência para teste: como a linha `InFlight` é um estado durável e independente da transação do empréstimo, um teste de integração não precisa segurar concorrência real para exercitar `request-in-flight` — basta inserir diretamente uma linha `idempotency_keys` com `state = InFlight` (mesma chave/corpo que a requisição de teste vai usar) antes de disparar a `POST /loans` via `HttpClient`. O filtro vê a colisão no `INSERT ON CONFLICT`, lê `state == InFlight` e devolve 409, de forma determinística — sem depender de timing entre duas requisições reais.

### `endpoint` gravado como o template da rota, não a URL completa

`endpoint = "POST /loans"` (constante), não `HttpContext.Request.Path`. A PK é `(key, endpoint)` porque o brief e o `CLAUDE.md` preveem reuso da tabela por outros endpoints idempotentes no futuro; usar o template evita colisão se a rota ganhar segmentos variáveis depois.

### Liberação da chave é um `DELETE`, não uma transição de estado

Quando o handler rejeita por regra de negócio ou lança, o filtro apaga a linha `InFlight` (`DELETE FROM idempotency_keys WHERE key = ... AND endpoint = ... AND state = 'InFlight'`) em vez de manter um terceiro estado (`Failed`). Isso mantém a máquina de estados em duas transições (`InFlight` → apagada, ou `InFlight` → `Completed`) e faz uma nova tentativa legítima cair direto no caminho de "não existe" do `INSERT ... ON CONFLICT`, sem precisar de um `WHERE state != 'Failed'` a mais em toda leitura.

### Configuração de `IEntityTypeConfiguration<IdempotencyKey>`

Segue o padrão de `LoanConfiguration`/`BookConfiguration`: chave composta via `HasKey(k => new { k.Key, k.Endpoint })`, `response_body` como `jsonb` (mesmo tipo de `AuditEvent.Payload`), `created_at_utc`/`expires_at_utc` como `timestamptz`. `IdempotencyKey` mora em `Domain/Idempotency/` — é uma entidade com invariante própria (estado `InFlight`/`Completed`), não uma tabela de apoio sem regra.

### Teste do último exemplar não muda; novo teste cobre a mesma chave

O cenário de `loan-management` "Last copy under concurrent load" (N=20, chaves distintas) continua validando a atomicidade do contador. Um teste de integração novo e separado cobre "Concurrent requests with the same key produce exactly one loan": mesmo padrão (`Barrier`, `HttpClient` por tarefa), mas com uma única `Idempotency-Key` e o mesmo corpo em todas as N requisições, livro com exemplares suficientes para não confundir os dois tipos de rejeição (o teste deve isolar dedup de idempotência de rejeição por indisponibilidade).

## Risks / Trade-offs

- [Sem expurgo automático, `idempotency_keys` cresce indefinidamente] → Aceito como limitação explícita desta change (fora do escopo do brief); `expires_at_utc` já grava a janela de 24h para uma change futura de expurgo (job ou `DELETE ... WHERE expires_at_utc < now()` periódico) usar sem migração adicional.
- [Chave `InFlight` cujo handler trava (crash do processo antes do `DELETE`/`UPDATE`) fica presa até expirar, bloqueando retries legítimos da mesma `Idempotency-Key` por até 24h] → Aceito: sem expurgo automático nesta change, não há como distinguir "ainda processando" de "processo morreu" sem heartbeat/timeout adicional, que o brief não pede. Cliente pode usar uma nova `Idempotency-Key` se precisar tentar de novo antes da expiração.
- [Hash calculado sobre bytes crus exige `Request.EnableBuffering()`, que mantém o corpo inteiro em memória (ou em disco, acima do limiar do ASP.NET Core) até o fim da requisição] → Aceito: corpos de `POST /loans` são pequenos (dois `Guid`s), sem risco prático de memória.
- [`response_body` como `jsonb` não preserva formatação byte-a-byte — o Postgres reformata o texto ao persistir/reler (ex.: acrescenta espaço após `:`), então o replay não é bit-idêntico ao que a "Alternativa: replay por reconsulta" descartada citava como vantagem do `response_body` literal] → Aceito: `jsonb` preserva o conteúdo semântico (mesmos campos, mesmos valores) exatamente, que é o que "mesmo corpo" no requisito exige; nenhum cliente HTTP real distingue respostas por espaçamento. Testes de replay comparam o corpo desserializado (`LoanResponse`), não a string bruta.
- [Argument binding do Minimal API lê o corpo de `POST /loans` para desserializar `CreateLoanRequest` **antes** do `IEndpointFilter` rodar — chamar `Request.EnableBuffering()` dentro do próprio filtro seria tarde demais, encontrando o stream já consumido] → Mitigado: `Program.cs` registra um middleware mínimo, condicional a `POST /loans`, que chama `EnableBuffering()` antes da fase de roteamento/binding alcançar o endpoint. Isso não reabre a decisão de manter a lógica de dedup apenas no filtro (`CLAUDE.md`: "aplicado somente ao endpoint `POST /loans` — não como middleware global") — o middleware só garante que o stream seja relível, sem nenhuma decisão de negócio; é condicional por path+method, não roda para nenhuma outra rota.
