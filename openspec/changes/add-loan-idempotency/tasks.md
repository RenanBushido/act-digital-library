## 1. Domain e persistência

- [x] 1.1 Criar `Domain/Idempotency/IdempotencyKey.cs` (entidade com `Key`, `Endpoint`, `RequestHash`, `State` (enum `InFlight`/`Completed`), `StatusCode`, `ResponseBody`, `ResourceId`, `CreatedAtUtc`, `ExpiresAtUtc`) e verificar que compila sem warnings
- [x] 1.2 Criar `Infrastructure/Persistence/IdempotencyKeyConfiguration.cs` (`IEntityTypeConfiguration<IdempotencyKey>`, `HasKey(k => new { k.Key, k.Endpoint })`, `response_body` como `jsonb`, `created_at_utc`/`expires_at_utc` como `timestamptz`, `state` com `HasConversion<string>()`) e registrar `DbSet<IdempotencyKey>` em `AppDbContext`
- [x] 1.3 Gerar a migration `AddLoanIdempotency` em `src/Library.Api/Migrations` (`dotnet ef migrations add`) e verificar que o `Up` cria a tabela `idempotency_keys` com a PK composta e que `dotnet ef migrations script` roda sem erro

## 2. Erros e contrato HTTP

- [x] 2.1 Adicionar `LoanErrors.IdempotencyKeyReuse()` (422, `idempotency-key-reuse`) e `LoanErrors.RequestInFlight()` (409, `request-in-flight`) em `Common/Errors/LoanErrors.cs` e verificar que cada um mapeia para o status/type corretos via teste unitário existente de `Error`/`ToProblem`, se houver, ou inspeção manual do `ErrorHttpResultExtensions`

## 3. Filtro de idempotência

- [x] 3.1 Reescrever `RequireIdempotencyKeyFilter` para, após validar a presença do header: chamar `Request.EnableBuffering()`, ler os bytes crus do corpo, calcular `request_hash = SHA256(bytes + "POST /loans")`, e tentar `INSERT ... ON CONFLICT DO NOTHING` na tabela `idempotency_keys` com `state = InFlight`
- [x] 3.2 Implementar a decisão pós-insert: se a linha foi inserida, chamar `next(context)`; se já existia com `request_hash` diferente, devolver 422 `idempotency-key-reuse`; se `state == Completed`, devolver a resposta armazenada (`status_code`, `response_body`) com header `Idempotency-Replayed: true`; se `state == InFlight`, devolver 409 `request-in-flight`
- [x] 3.3 Após `next(context)`, se o resultado não for 2xx (exceção capturada ou `IResult` de erro), apagar a linha `InFlight` correspondente antes de propagar a resposta, e verificar com teste de integração que uma rejeição de negócio libera a chave para nova tentativa

## 4. Gravação da resposta no handler

- [x] 4.1 Em `CreateLoan.HandleAsync`, dentro da transação já existente e após montar `LoanResponse`, serializar o corpo da resposta e atualizar a linha `idempotency_keys` (`state = Completed`, `status_code = 201`, `response_body`, `resource_id = loan.Id`) antes do `CommitAsync`
- [x] 4.2 Verificar, com teste de integração, que empréstimo e gravação da chave idempotente commitam juntos (nenhum estado onde um existe sem o outro)

## 5. Testes de integração

- [x] 5.1 Teste: repetir a mesma `POST /loans` (mesma `Idempotency-Key`, mesmo corpo) devolve a resposta original com `Idempotency-Replayed: true`, sem criar segundo empréstimo nem reduzir `available_copies` de novo
- [x] 5.2 Teste: mesma `Idempotency-Key` com corpo diferente devolve 422 `idempotency-key-reuse`, e o empréstimo original permanece intacto
- [x] 5.3 Teste: com uma linha `idempotency_keys` inserida previamente com `state = InFlight` para a mesma chave/corpo (gravada direto via `AppDbContext` no arranjo do teste, sem precisar de concorrência real — ver design.md), uma `POST /loans` com essa `Idempotency-Key` devolve 409 `request-in-flight`
- [x] 5.4 Teste: uma `POST /loans` rejeitada por regra de negócio (livro inativo ou sem exemplar) libera a chave, e uma nova tentativa com a mesma `Idempotency-Key` após a condição ser corrigida é reavaliada do zero e pode suceder
- [x] 5.5 Teste de concorrência: `Barrier` com N=20 requisições, todas com a **mesma** `Idempotency-Key` e o mesmo corpo, livro com exemplares suficientes; asserções: exatamente um 201, as demais recebem replay (`Idempotency-Replayed: true`) ou `request-in-flight`, `available_copies` decrementado em exatamente 1, e um único empréstimo criado
- [x] 5.6 Verificar que o teste de concorrência já existente ("Last copy under concurrent load", N=20 chaves distintas) continua passando sem alteração de comportamento

## 6. Revisão final

- [x] 6.1 Rodar a suíte completa (`dotnet test`) e confirmar que todos os testes unitários e de integração passam, incluindo os cenários GIVEN/WHEN/THEN da spec `loan-management` modificada nesta change
