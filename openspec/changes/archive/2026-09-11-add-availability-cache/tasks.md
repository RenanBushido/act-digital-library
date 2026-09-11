## 1. Configuração e registro do Redis

- [x] 1.1 Adicionar `CacheOptions` (`AvailabilityTtlSeconds` = 60, `ListTtlSeconds` = 120) e a seção `Cache` em `appsettings.json`/`appsettings.Development.json`, e registrar `services.Configure<CacheOptions>(configuration.GetSection("Cache"))` — verificar lendo a configuração em teste de integração e confirmando os valores padrão
- [x] 1.2 Em `Extensions/CachingExtensions.cs`, registrar `IConnectionMultiplexer` como singleton a partir da connection string `Redis`, e passar `ConnectionMultiplexerFactory` para `AddStackExchangeRedisCache` reutilizando essa instância (sem `InstanceName`) — verificar que a aplicação sobe e que só existe uma conexão ao Redis (checar log/contagem de conexões nos testes de integração)

## 2. `BookCache`

- [x] 2.1 Criar `Infrastructure/Caching/BookCache.cs`, classe concreta sem interface, recebendo `IDistributedCache`, `IConnectionMultiplexer`, `IOptions<CacheOptions>`, `ILogger<BookCache>`; implementar `GetAvailabilityAsync`/`SetAvailabilityAsync`/`InvalidateAvailabilityAsync(Guid bookId)` usando a chave `library:book:{id}:availability` — verificar com teste de integração que um valor gravado é lido de volta e que `InvalidateAvailabilityAsync` remove a chave
- [x] 2.2 Implementar `GetListAsync`/`SetListAsync`/`InvalidateListAsync` em `BookCache`: resolve a versão atual em `library:books:list:version` (0 se ausente), monta a chave `library:books:list:v{versão}:{hash}`, e `InvalidateListAsync` faz `INCR` nessa chave de versão via `IConnectionMultiplexer` — verificar com teste de integração que, depois de `InvalidateListAsync`, uma leitura com a mesma combinação de filtros já não bate na entrada antiga
- [x] 2.3 Implementar o hash de filtros (`title`, `author`, `isbn`, `includeInactive`, `page`, `pageSize`), normalizado e em ordem fixa, com SHA-256 — verificar com teste unitário que duas combinações de filtro equivalentes (mesma diferença só de espaço/maiúscula) produzem o mesmo hash e que combinações diferentes produzem hashes diferentes
- [x] 2.4 Fazer toda leitura, escrita e invalidação em `BookCache` tratar falha do Redis com `try/catch` + `LogWarning`, nunca propagando exceção — verificar com teste de integração que parar o container Redis (`ApiFixture.StopRedisAsync`) não derruba nenhuma chamada a `BookCache`
- [x] 2.5 Remover `Common/CacheReadThrough.cs` e `Common/CacheDefaults.cs` (substituídos por `BookCache`) e confirmar que o build não referencia mais essas classes

## 3. Filtros de listagem e handlers de `Books`

- [x] 3.1 Estender `ListBooks` para aceitar `title`, `author`, `isbn`, `includeInactive` como query params opcionais e aplicar os filtros correspondentes na query EF Core (mantendo a exclusão de inativos quando `includeInactive` é ausente/false) — verificar com teste de integração que cada filtro isolado restringe o resultado corretamente
- [x] 3.2 Trocar o uso de `CacheReadThrough`/`IDistributedCache` em `ListBooks.HandleAsync` e `GetBookAvailability.HandleAsync` pelas chamadas correspondentes de `BookCache` — verificar com teste de integração que a segunda chamada idêntica não gera nova consulta ao banco (por exemplo, checando que o valor retornado é igual após alterar o banco diretamente, sem passar pelo endpoint de escrita)
- [x] 3.3 Atualizar `CreateBook`, `UpdateBook`, `DeactivateBook` para chamar `BookCache.InvalidateAvailabilityAsync` (quando aplicável) e `BookCache.InvalidateListAsync` depois do `SaveChangesAsync`, substituindo as chamadas antigas a `CacheReadThrough.RemoveAsync` — verificar com os testes de integração já existentes de invalidação (`GET /books` reflete criação) mais um novo teste cobrindo uma combinação de filtro não padrão
- [x] 3.4 Teste de integração: cachear `GET /books/{id}/availability`, alterar o livro via `PATCH /books/{id}` (ou desativar via `DELETE /books/{id}`), e confirmar que a leitura seguinte de `GET /books/{id}/availability` reflete o estado novo, sem depender do TTL — cobre o cenário "Read after book creation, update or deactivation reflects the new state" da spec, hoje sem teste mesmo no código atual
- [x] 3.5 Teste de integração: cachear `GET /books?title=X` e `GET /books?author=Y` (duas combinações de filtro distintas), fazer uma leitura que confirme as duas em cache, então buscar cada uma de novo e confirmar que o conteúdo de uma não vazou para a outra — cobre o cenário "Different filter combinations are cached independently"
- [x] 3.6 Corrigir `ListBooks` para validar `isbn` antes de consultar cache/banco: um valor que normaliza para vazio (ex.: `isbn=---`) retornava 500 (achado da revisão pré-archive) em vez de 400 `validation-failed` — verificado com teste de integração

## 4. Invalidação nos handlers de `Loans`

- [x] 4.1 Em `CreateLoan.HandleAsync`, chamar `BookCache.InvalidateAvailabilityAsync(request.BookId)` e `BookCache.InvalidateListAsync()` depois de `transaction.CommitAsync` — verificar com teste de integração: cachear a disponibilidade, criar um empréstimo, e confirmar que a próxima leitura reflete `available_copies` decrementado
- [x] 4.2 Repetir para `ReturnLoan.HandleAsync` e `CancelLoan.HandleAsync`, invalidando depois do commit — verificar com testes de integração equivalentes para devolução e cancelamento, confirmando `available_copies` incrementado na leitura seguinte
- [x] 4.3 Teste de integração por operação (criação, devolução, cancelamento de empréstimo): cachear `GET /books` (listagem, não só a disponibilidade individual), executar a operação de empréstimo, e confirmar que a leitura seguinte de `GET /books` já reflete `available_copies` atualizado para o livro afetado — cobre o cenário "Cache is invalidated after a loan is created, returned or cancelled" no nível da listagem, distinto da cobertura de 4.1/4.2 (que só verificam o endpoint de disponibilidade)

## 5. Testes de integração centrais desta change

- [x] 5.1 Teste "leitura acelerada": duas chamadas seguidas a `GET /books/{id}/availability` dentro do TTL retornam o mesmo valor sem uma segunda consulta ao banco (alterar o livro direto no banco entre as chamadas e confirmar que a segunda ainda reflete o valor cacheado, não o novo) — mesmo padrão para `GET /books`
- [x] 5.2 Teste "invalidação alcança combinações diferentes": cachear `GET /books` com dois filtros distintos, criar um livro, e confirmar que ambas as combinações refletem o novo livro na leitura seguinte
- [x] 5.3 Teste "leitura após devolução": emprestar um livro, cachear a disponibilidade, devolver o empréstimo, e confirmar que a leitura seguinte de `GET /books/{id}/availability` reflete `available_copies` incrementado
- [x] 5.4 Teste "cache envenenado": gravar manualmente no Redis, na chave `library:book:{id}:availability`, uma disponibilidade maior que a real para um livro com `available_copies == 0`, e confirmar que `POST /loans` ainda responde 409 `no-copy-available` e não cria empréstimo
- [x] 5.5 Estender `CacheDegradationTests.cs` com um caso de degradação na invalidação: parar o Redis (`StopRedisAsync`) e confirmar que `POST /books`, `PATCH /books/{id}`, `DELETE /books/{id}`, `POST /loans`, `POST /loans/{id}/return` e `POST /loans/{id}/cancel` continuam respondendo com sucesso

## 6. Verificação final

- [x] 6.1 Rodar a suíte completa (`dotnet test`) e confirmar que todo cenário GIVEN/WHEN/THEN da spec `catalog-endpoints` (delta desta change) tem um teste correspondente
- [x] 6.2 Confirmar que `TreatWarningsAsErrors` não quebra o build (`dotnet build`) depois da remoção de `CacheReadThrough`/`CacheDefaults` e da adição dos novos arquivos
