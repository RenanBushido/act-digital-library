## Why

`GET /books` e `GET /books/{id}/availability` já leem do PostgreSQL a cada chamada, e a implementação atual de cache é provisória: usa `IDistributedCache` cru em cada handler, sem versionamento da listagem (páginas não padrão dependem só do TTL), sem cobrir filtros de busca, e sem nenhuma invalidação nas três operações de empréstimo — `POST /loans`, `POST /loans/{id}/return` e `POST /loans/{id}/cancel` alteram `available_copies` sem tocar o cache. Isso permite que uma leitura de disponibilidade fique até 60s desatualizada depois de um empréstimo ser criado ou devolvido. Esta change substitui essa implementação pela solução fixada no CLAUDE.md: uma classe `BookCache` concreta, chave de listagem versionada, e invalidação nas seis operações que afetam disponibilidade.

## What Changes

- Registra o `IConnectionMultiplexer` do Redis separadamente e o compartilha com `AddStackExchangeRedisCache` via `ConnectionMultiplexerFactory`, sem `InstanceName`.
- Introduz a classe concreta `BookCache` (`src/Library.Api/Infrastructure/Caching/BookCache.cs`), sem interface, centralizando nomes de chave, TTL e tratamento de falha de leitura/invalidação. Substitui o uso direto de `IDistributedCache`/`CacheReadThrough` nos handlers de `Books`.
- **BREAKING** (contrato de chave, não de API HTTP): renomeia a chave de disponibilidade de `books:{id}:availability` para `book:{id}:availability` com prefixo explícito `library:`, e substitui a chave de listagem `books:list:page=X:size=Y` por `books:list:v{versão}:{hash dos filtros}`, com a versão em `books:list:version` incrementada via `INCR` a cada invalidação — chaves antigas ficam órfãs e expiram pelo TTL, sem `KEYS`/`SCAN`.
- `GET /books` passa a aceitar os filtros `title`, `author`, `isbn` e `includeInactive`, todos incluídos (normalizados, em ordem fixa) no hash da chave de listagem.
- TTLs passam a ser configuráveis em `Cache:*` (`Cache:AvailabilityTtlSeconds` = 60, `Cache:ListTtlSeconds` = 120, com esses valores como padrão).
- Adiciona invalidação de cache (disponibilidade do livro + versão da listagem) em `POST /loans`, `POST /loans/{id}/return` e `POST /loans/{id}/cancel`, depois do commit da transação — hoje essas três operações não tocam o cache.
- Corrige a invalidação de `POST /books`, `PATCH /books/{id}` e `DELETE /books/{id}` para usar a nova chave versionada, em vez de invalidar apenas a página/tamanho padrão.
- Adiciona testes de integração com Redis real: leitura acelerada (hit sem tocar o banco), invalidação alcançando combinações de filtro diferentes, leitura após devolução refletindo o novo disponível, cache divergente não influenciando a decisão de empréstimo, e degradação nas duas direções (leitura e invalidação) com Redis indisponível.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `catalog-endpoints`: os requisitos "List books" e "Get book availability" passam a fixar o comportamento de cache (chave, TTL, filtros cobertos, ausência de influência sobre decisão de empréstimo) e a exigir que as seis operações que afetam disponibilidade — incluindo as três de empréstimo, hoje não cobertas — invalidem a listagem e a disponibilidade cacheadas.

## Impact

- **Código**: `Extensions/CachingExtensions.cs` (registro do multiplexer), novo `Infrastructure/Caching/BookCache.cs`, `Features/Books/{ListBooks,GetBookAvailability,CreateBook,UpdateBook,DeactivateBook}.cs`, `Features/Loans/{CreateLoan,ReturnLoan,CancelLoan}.cs`, `appsettings.json` (seção `Cache`). Remove `Common/CacheReadThrough.cs` e `Common/CacheDefaults.cs`, hoje usados só pelos handlers de `Books`.
- **Testes**: novos testes de integração em `tests/Library.IntegrationTests/Features/Books/` e `Features/Loans/`; `CacheDegradationTests.cs` existente é estendido para cobrir degradação na invalidação, além da leitura.
- **Sem migração de banco**: nenhuma mudança de schema PostgreSQL.
- **Sem mudança de contrato HTTP observável**, exceto a adição dos parâmetros de filtro opcionais em `GET /books`.
