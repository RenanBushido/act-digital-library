## Context

Ver proposal.md - Why. `GET /books` e `GET /books/{id}/availability` hoje leem de `IDistributedCache` diretamente nos handlers (`Features/Books/ListBooks.cs`, `Features/Books/GetBookAvailability.cs`), com a lógica de leitura/escrita duplicada em `Common/CacheReadThrough.cs` e TTL fixo em `Common/CacheDefaults.cs` (60s, único, para as duas chaves). A invalidação hoje só existe em `CreateBook`, `UpdateBook` e `DeactivateBook`, e só remove a chave de `page=1:size=20` — outras páginas dependem do TTL. `Features/Loans/{CreateLoan,ReturnLoan,CancelLoan}.cs` não tocam o cache. `Extensions/CachingExtensions.cs` já registra `AddStackExchangeRedisCache` sem `InstanceName`, o que preserva; falta o `IConnectionMultiplexer` dedicado.

## Goals / Non-Goals

**Goals:**
- Uma única classe concreta `BookCache` como porta de entrada para toda leitura/escrita/invalidação de cache de livros.
- Listagem cacheada por combinação de filtros, com invalidação que alcança todas as combinações sem `KEYS`/`SCAN`.
- As seis operações que afetam disponibilidade (criação/alteração/desativação de livro, criação/devolução/cancelamento de empréstimo) invalidam o cache depois do commit.
- Degradação de Redis nunca falha a requisição, em leitura nem em invalidação.

**Non-Goals:**
- Métricas de cache, health check de Redis, logs estruturados além do `LogWarning` já convencionado — fora de escopo desta change (ver proposal.md).
- Proteção contra cache stampede (`HybridCache` com L1) — limitação aceita e documentada no README, não resolvida aqui.
- Cache de qualquer endpoint fora de `GET /books` e `GET /books/{id}/availability`.

## Decisions

### `BookCache` concreta, sem interface
Centraliza nomes de chave, TTL e tratamento de falha, como fixado no CLAUDE.md. Vive em `src/Library.Api/Infrastructure/Caching/BookCache.cs`, seguindo a organização de pastas do projeto (`Infrastructure/Caching/` — `BookCache`); o registro do `IConnectionMultiplexer` e do `ConnectionMultiplexerFactory` fica em `Extensions/CachingExtensions.cs`, junto do `AddStackExchangeRedisCache` já existente ali. Recebe `IDistributedCache`, `IConnectionMultiplexer` (só para o `INCR` da versão da listagem — `IDistributedCache` não expõe operações atômicas), `IOptions<CacheOptions>` e `ILogger<BookCache>`. Substitui `Common/CacheReadThrough.cs` e `Common/CacheDefaults.cs`, que são removidos: hoje só servem `Books`, e a nova classe cobre o mesmo papel com tratamento de TTL diferenciado por tipo de chave.

Métodos expostos: `GetAvailabilityAsync`/`SetAvailabilityAsync`/`InvalidateAvailabilityAsync(bookId)`, `GetListAsync`/`SetListAsync(filters)`/`InvalidateListAsync()`. São pares get/set simples (cache-aside), não um `GetOrCreateAsync` com factory como o `CacheReadThrough` atual: `BookCache` não conhece o PostgreSQL nem como montar a resposta. Cabe ao handler orquestrar o miss — chamar `Get...Async`, e só em caso de `null` consultar o banco e chamar `Set...Async` com o resultado. Os handlers chamam só esses métodos — nenhum handler toca `IDistributedCache` ou `IConnectionMultiplexer` diretamente.

### Chave de disponibilidade direta, chave de listagem versionada
`book:{id}:availability`, TTL 60s, invalidada com `RemoveAsync` direto — cardinalidade é uma chave por livro, então apagar é barato e imediato.

`books:list:v{versão}:{hash}`, TTL 120s. A versão vive em `books:list:version` (Redis, sem TTL). Invalidar = `INCR books:list:version`; toda leitura resolve a versão atual antes de montar a chave, então uma leitura em andamento nunca gruda numa versão velha, e as chaves da versão anterior ficam órfãs e expiram pelo TTL. Alternativa descartada: apagar cada combinação de filtro já vista exigiria manter um índice de chaves ativas (outra estrutura para manter consistente) ou `KEYS`/`SCAN` (proibido pelo CLAUDE.md — bloqueia o Redis em produção).

Prefixo `library:` escrito explicitamente dentro de `BookCache` nas duas chaves (`library:book:{id}:availability`, `library:books:list:...`), incluindo a chave de versão. `InstanceName` continua não usado no `AddStackExchangeRedisCache`, para não ter duas convenções de prefixo.

### Hash de filtros da listagem
`title`, `author`, `isbn` e `includeInactive`, mais `page`/`pageSize`, normalizados (trim, lower-case em texto, `includeInactive` como `0`/`1`) e serializados em ordem fixa de campo antes do SHA-256. Isso decide o **nome da chave** apenas — o hash não é armazenado nem comparado a request_hash de idempotência, que é um mecanismo não relacionado.

`ListBooks` passa a aceitar `title`, `author`, `isbn`, `includeInactive` como query params opcionais e filtra a query EF Core de acordo. Por que a listagem expõe disponibilidade: o corpo de `PagedBookResponse` já inclui `availableCopies` por item (reaproveita `BookResponse.From`), então cachear a listagem sem invalidá-la nas mesmas seis operações criaria uma segunda fonte de disponibilidade defasada, inconsistente com a de `GET /books/{id}/availability` — por isso a listagem entra na mesma invalidação, e não só a chave de disponibilidade individual.

### Invalidação depois do commit, incluindo os handlers de empréstimo
`CreateLoan`, `ReturnLoan` e `CancelLoan` já abrem uma transação explícita (`BeginTransactionAsync`) para as duas escritas condicionais (livro + empréstimo). A chamada a `BookCache.InvalidateAvailabilityAsync`/`InvalidateListAsync` entra logo depois de `transaction.CommitAsync`, nunca dentro da transação — commitar e só então invalidar evita que outra réplica releia a linha ainda não confirmada e repopule o cache com o valor antigo (mesma razão já fixada no CLAUDE.md para as operações de livro, agora aplicada aqui).

### Escrita-through descartada; TTL curto sem invalidação descartado
Write-through (a operação já escreve o novo valor no cache, em vez de invalidar) foi descartado: exigiria que cada handler reconstruísse a mesma projeção de resposta usada pela leitura (incluindo a forma paginada/filtrada da listagem), duplicando lógica de serialização e criando mais um lugar para divergir do banco. Invalidar é mais simples e a próxima leitura já reconstrói o valor correto sob demanda.

TTL curto sem invalidação explícita (por exemplo, 2-5s) foi descartado porque não atende ao requisito "a próxima leitura reflete o estado novo" — ainda existiria uma janela garantida de dado velho, em vez de invalidação determinística.

`HybridCache` (L1 em memória) foi descartado por reintroduzir estado em processo: o desafio declara que nenhuma garantia de correção pode depender disso, e a leitura de disponibilidade já não decide empréstimo — o ganho de latência do L1 não justifica a complexidade adicional aqui. Fica registrado como evolução possível no README, sem stampede protection.

## Risks / Trade-offs

- [Duas leituras Redis por invalidação de listagem (resolver versão + potencial GC de chaves órfãs)] → aceito: é O(1) por leitura, e chaves órfãs expiram sozinhas pelo TTL, sem custo de limpeza ativa.
- [Falha do `INCR` de versão deixa a leitura seguinte servir uma chave já invalidada em teoria, mas antes de expirar] → o `INCR` roda dentro do mesmo tratamento de falha de invalidação do CLAUDE.md: se falhar, `LogWarning` e a requisição segue — o TTL de 120s é a rede de segurança, mesma garantia já aceita para a chave de disponibilidade.
- [`IConnectionMultiplexer` fica um segundo ponto de configuração de conexão ao Redis, além de `AddStackExchangeRedisCache`] → mitigado por compartilhar a mesma connection string via `ConnectionMultiplexerFactory`, como fixado no CLAUDE.md — não há duas fontes de verdade para o endereço do Redis.

## Migration Plan

Sem migração de schema. Deploy substitui as chaves antigas (`books:{id}:availability`, `books:list:page=...`) pelas novas (`library:book:{id}:availability`, `library:books:list:v...`) — não há necessidade de migrar dados em Redis: as chaves antigas simplesmente param de ser escritas e expiram pelo TTL já configurado nelas. Rollback é reverter o deploy; nenhuma limpeza manual de Redis é necessária.
