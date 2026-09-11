## Why

`POST /loans` hoje só valida a presença do header `Idempotency-Key` (fixado por `add-loan-concurrency`); repetir a mesma requisição — por retry de cliente, timeout, ou reenvio manual — cria um segundo empréstimo e consome um segundo exemplar. Sem deduplicação real, o header não cumpre a promessa de idempotência que seu próprio nome anuncia.

## What Changes

- Tabela `idempotency_keys` (PK `(key, endpoint)`) e sua migration, guardando `request_hash`, `state` (`InFlight` | `Completed`), `status_code`, `response_body`, `resource_id`, `created_at_utc`, `expires_at_utc` (janela de 24h).
- `RequireIdempotencyKeyFilter` evolui de "só valida presença" para reservar a chave (`INSERT … ON CONFLICT DO NOTHING`) e decidir entre: seguir para o handler, devolver o replay armazenado (`Idempotency-Replayed: true`), rejeitar chave reutilizada com corpo diferente (422), ou rejeitar requisição em voo (409 `request-in-flight`).
- `CreateLoan` passa a gravar a resposta (`status_code`, `response_body`, `resource_id`, `state = Completed`) na mesma transação do empréstimo, e o filtro libera a chave (permite nova tentativa) quando o handler rejeita por regra de negócio ou lança exceção.
- Novo teste de integração de concorrência, separado do teste do último exemplar já existente (`add-loan-concurrency`, N chaves distintas competindo pelo exemplar): N requisições simultâneas com a **mesma** `Idempotency-Key` e o mesmo corpo devem produzir exatamente um empréstimo — deduplicação real, não N-1 rejeições de negócio. O teste existente permanece como está.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `loan-management`: o requisito "Create loan requires an Idempotency-Key header" ganha os requisitos de deduplicação real — repetição bem-sucedida devolve replay, chave reutilizada com corpo diferente é rejeitada, requisição em voo é rejeitada, e concorrência com a mesma chave produz exatamente um empréstimo.

## Impact

- Novas tabelas/migration: `idempotency_keys`.
- `Features/Loans/RequireIdempotencyKeyFilter.cs`: reescrito para reservar/decidir, não só validar presença.
- `Features/Loans/CreateLoan.cs`: grava resposta idempotente na transação existente.
- `Infrastructure/Persistence/`: nova `IEntityTypeConfiguration` para `idempotency_keys`.
- `Common/Errors/LoanErrors.cs`: novos erros `idempotency-key-reuse` (422) e `request-in-flight` (409).
- Testes de integração de `Features/Loans` (Testcontainers): cenários de replay, reuso com corpo diferente, requisição em voo, e concorrência com mesma chave.
