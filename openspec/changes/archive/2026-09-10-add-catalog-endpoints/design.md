## Context

`catalog-domain` já fixou `Book`, `Isbn`, `User`, `Email`, `BookConfiguration`/`UserConfiguration` e `LibraryDbContext`, todos hoje em `src/Library.Api/Features/{Books,Users}` e `src/Library.Api/Infrastructure`. Depois disso, o `CLAUDE.md` ganhou uma seção nova, "Organização do código", que separa `Domain/<Entidade>/` (entidades e value objects), `Infrastructure/Persistence/` (`AppDbContext` + `IEntityTypeConfiguration`) e proíbe interface para implementação única. Nenhum endpoint HTTP existe ainda — este é o primeiro código que precisa satisfazer a seção **Contrato HTTP** do `CLAUDE.md` (Problem Details com `correlationId`/`traceId`, `Result<T>` na borda).

## Goals / Non-Goals

**Goals:**
- Migrar `Book`/`Isbn`/`User`/`Email`/`LibraryDbContext`/configurações para a estrutura declarada em "Organização do código", sem mudar comportamento.
- Expor `GET/POST/PATCH/DELETE /books` e `GET /books/{id}/availability` conforme a tabela Contrato HTTP.
- Introduzir o mínimo de infraestrutura compartilhada (`Common/`) que qualquer endpoint precisa: `Result<T>`, catálogo de erros, correlação, Problem Details.
- Cache Redis só nas duas leituras que o `CLAUDE.md` autoriza (`GET /books`, `GET /books/{id}/availability`).

**Non-Goals:**
- `Loans`, `Users` (endpoints), `Audit` — pastas próprias em "Estrutura", fora do escopo desta change. Isso inclui os eventos `BookCreated`/`BookUpdated`/`BookDeactivated` da seção Auditoria: não há `audit_events`/`AuditEvent` ainda, então esses eventos não são emitidos por esta change. Registrado como risco abaixo.
- Métricas OpenTelemetry (`library.loans.*` é de `Loans`) e health checks — não dependem de `Book`.
- Idempotência (`Idempotency-Key`) — o `CLAUDE.md` só a exige em `POST /loans`.
- Edição de ISBN em `PATCH /books/{id}` — ISBN é imutável após a criação (nenhum cenário do `catalog-domain` ou desta spec pede alterar ISBN; reabrir isso é uma decisão de outra change).

## Decisions

### Migração de estrutura primeiro, endpoints depois

`Book.cs`/`Isbn.cs` → `Domain/Book/`; `User.cs`/`Email.cs` → `Domain/User/`; `LibraryDbContext.cs` → `Infrastructure/Persistence/AppDbContext.cs` (renomeado); `BookConfiguration.cs`/`UserConfiguration.cs` → `Infrastructure/Persistence/`. Só o nome da classe e o caminho mudam — construtores, invariantes e mapeamento EF Core continuam idênticos. As migrations existentes (`20260909234508_InitialCatalogSchema`) não mudam de conteúdo; só o atributo `[DbContext(typeof(LibraryDbContext))]` no `.Designer.cs` e o nome da classe `LibraryDbContextModelSnapshot` são atualizados para `AppDbContext`/`AppDbContextModelSnapshot`, confirmando com `dotnet ef migrations has-pending-model-changes` que o schema não mudou.

`DomainException` vai para `Common/DomainException.cs`: não é um value object de uma entidade específica (não cabe em `Domain/<Entidade>/`) nem parte da composição de DI (não é `Extensions/`) — é exatamente o tipo de coisa que "atravessa features" que `Common/` descreve. **Nota de consistência**: o `CLAUDE.md` (seção Estrutura, antes de "Organização do código" existir) ainda tem uma frase dizendo que `Infrastructure` guarda `DomainException` — desatualizada desde que a seção "Organização do código" foi escrita. A task 6.2 corrige essa frase como parte desta change, para o `CLAUDE.md` não ficar com duas respostas diferentes para "onde mora `DomainException`".

### Arquivos que esta change cria

**Migração (mover/renomear, sem novo comportamento):**

- `Domain/Book/Book.cs`, `Domain/Book/Isbn.cs`
- `Domain/User/User.cs`, `Domain/User/Email.cs`
- `Infrastructure/Persistence/AppDbContext.cs`, `BookConfiguration.cs`, `UserConfiguration.cs`
- `Common/DomainException.cs`

**Common (novo, mínimo para qualquer endpoint responder no formato do Contrato HTTP):**

- `Common/Errors/Error.cs` — tipo simples `(string Type, int StatusCode, string Title)` usado pelo catálogo de erros.
- `Common/Errors/BookErrors.cs` — catálogo dos erros desta change: `NotFound`, `Inactive`, `IsbnDuplicate`, `InsufficientAvailableCopies`, `ValidationFailed`.
- `Common/Errors/ErrorHttpResultExtensions.cs` — `Error.ToProblem()`, convertendo o erro tipado direto em `IResult` (`TypedResults.Problem`).
- `Common/CorrelationIdMiddleware.cs` — lê/gera `X-Correlation-Id`, coloca no escopo de log e no header de resposta.
- `Common/Pagination/PagedResult.cs` — envelope `{ items, page, pageSize, totalCount }` para `GET /books`.

**Extensions (novo):**

- `Extensions/CachingExtensions.cs` — `AddApiCaching` registrando `IDistributedCache` sobre Redis (`AddStackExchangeRedisCache`), mesmo padrão de `DatabaseExtensions.AddApiDatabase`.
- `Extensions/ProblemDetailsExtensions.cs` — `AddApiProblemDetails`, customizando `CustomizeProblemDetails` para incluir `correlationId`/`traceId`.

**Features/Books (novo):**

- `Features/Books/BooksEndpoints.cs` — `MapGroup("/books")`, só roteamento.
- `Features/Books/ListBooks.cs`, `GetBook.cs`, `GetBookAvailability.cs`, `CreateBook.cs`, `UpdateBook.cs`, `DeactivateBook.cs` — um arquivo por caso de uso (request + validação + handler `static`).
- `Features/Books/Contracts/BookResponse.cs`, `BookAvailabilityResponse.cs`, `PagedBookResponse.cs` — DTOs de resposta com `From(entidade)`.

### `Error` + `IResult` no lugar de `Result<T>`

O plano inicial desta change previa um tipo `Result`/`Result<T>` genérico (sucesso ou erro tipado) para os handlers retornarem, e cada endpoint converteria esse `Result` num `IResult` HTTP no fim. Na implementação, esse wrapper ficou sem nenhum consumidor: cada handler já decide localmente entre um `TypedResults.Ok/Created/NoContent` (sucesso) ou um `Error.ToProblem()` (falha), ambos já `IResult` — o `Result<T>` intermediário não adicionava nada que os dois `return` diretos não resolvessem, só uma alocação e uma conversão a mais. `Common/Result.cs` foi removido; `Error` (o tipo de erro tipado) e `Common/Errors/ErrorHttpResultExtensions.cs` (`Error.ToProblem()`) são o que efetivamente cumpre "`Result<T>` para regra de negócio; exceção só para o inesperado" do `CLAUDE.md`: a regra de negócio nunca lança exceção para sinalizar falha esperada (exceto o `DomainException` de invariante, tratado no próprio handler), e a exceção genuína (`DbUpdateException` de violação de unicidade) é a única capturada.

### Por que não existem `IBookRepository` nem `IBookService`

O `CLAUDE.md` proíbe interface para implementação única e diz explicitamente "`DbSet` já é repositório e `DbContext` já é unidade de trabalho". Cada handler em `Features/Books/*.cs` recebe `AppDbContext` direto por parâmetro (injeção posicional do Minimal API) e usa `AppDbContext.Books` como o `DbSet<Book>` já é. Não há um único caso em que uma segunda implementação de acesso a dados substituiria o EF Core nesta change — não há teste que precise de um dublê de repositório (os testes de integração já usam Postgres real via Testcontainers, não mock) nem um cenário de troca de provedor. `TimeProvider` e `IDistributedCache`, que o `CLAUDE.md` cita como as abstrações válidas, continuam sendo os únicos pontos de indireção: ambos têm implementações reais alternativas usadas em teste (`FixedTimeProvider`) e em produção (Redis vs. nenhum cache), o que justifica a interface. Um `IBookService` também não se justifica: os casos de uso já são o `Features/Books/<CasoDeUso>.cs` que a Organização do código pede — um `IBookService` seria uma camada extra encapsulando exatamente esses handlers, sem nenhum consumidor alternativo.

### Paginação de `GET /books`

Query params `page` (padrão 1) e `pageSize` (padrão 20, máximo 100) — não há requisito explícito no `CLAUDE.md` além de `Common/` citar "paginação" como preocupação transversal; valores padrão e máximo são uma escolha razoável registrada aqui, ajustável sem mudar a spec se o padrão se mostrar errado.

### Regra de redução de exemplares

Ao atualizar `total_copies`, o delta (`novoTotal - totalAtual`) é aplicado também a `available_copies`. Se o resultado for negativo, a operação é rejeitada com `insufficient-available-copies` antes de qualquer escrita — mesmo padrão de `ExecuteUpdateAsync` condicional que `catalog-domain`/Concorrência já estabelece para não depender de leitura-depois-escrita.

### `GET /books` exclui livros inativos por padrão

Um livro desativado (`is_active == false`) mantém histórico e aceita devolução, mas não é mais parte do "catálogo disponível" — a listagem pública (`GET /books`) só retorna livros ativos. Não há parâmetro de filtro (`includeInactive` ou semelhante) nesta change: nenhum cenário pede consultar livros inativos em massa, e adicionar esse filtro sem um consumidor real seria escopo não pedido. `GET /books/{id}` e `GET /books/{id}/availability` continuam retornando livros inativos normalmente (com `isActive: false` no corpo) — a exclusão é só da listagem.

### Pastas de teste unitário usam sufixo `Tests` na folha (`Domain/BookTests/`, `Domain/UserTests/`)

Descoberto durante a implementação: nomear o namespace de teste exatamente `Library.UnitTests.Domain.Book` colide com o tipo `Book` trazido por `global using Library.Api.Domain.Book;` — o compilador resolve `Book.Create(...)` como o namespace `Library.UnitTests.Domain.Book` em vez do tipo, e falha com `CS0234`. A pasta/namespace folha ganhou o sufixo `Tests` (`BookTests`/`UserTests`) para não colidir, mantendo o espelhamento pretendido pela task 1.5 sem replicar o nome exato do tipo.

### Testes de integração HTTP precisam de `WebApplicationFactory` + Redis real

A seção Testes do `CLAUDE.md` exige `WebApplicationFactory` e "Postgres e Redis reais" para testes de integração — até agora (`catalog-domain`) os testes de integração só exercitavam o `DbContext` direto, sem nenhuma app HTTP no ar, porque não havia endpoint. Esta change introduz `ApiFixture` (`WebApplicationFactory<Program>` + Testcontainers de Postgres e Redis) especificamente para poder fazer requisições HTTP reais e checar status code/Problem Details, algo que a fixture antiga (`PostgresFixture`) não fazia e não precisa fazer sozinha (ela continua servindo aos testes de `catalog-domain`, que não mudam). Alternativa considerada: reaproveitar só o Postgres de `PostgresFixture` e adicionar Redis "por fora" sem subir a app via `WebApplicationFactory`, testando os handlers diretamente — rejeitada porque não verificaria o pipeline HTTP real (middleware de correlação, Problem Details, roteamento), que é justamente o que esta change adiciona.

### Remoção de `app.UseHttpsRedirection()`

Descoberto durante a implementação: `app.UseHttpsRedirection()` estava no esqueleto do `Program.cs` desde antes desta change, mas `docker-compose.yml` só expõe a API via `ASPNETCORE_HTTP_PORT` (sem porta HTTPS) — ou seja, TLS não é terminado no container. Com o middleware ativo, o `TestServer`/qualquer cliente batendo em HTTP recebia um redirect para `https://`, e um `HttpClient` (que segue redirect por padrão) reenviava a requisição original — duplicando `POST`/`PATCH`/`DELETE` silenciosamente. Isso só ficou visível agora porque esta é a primeira change com endpoints de escrita reais; o bug já existia. Removida a chamada: este projeto termina TLS fora do processo (proxy/ingress), consistente com `ASPNETCORE_HTTP_PORT` no `docker-compose.yml` e com o padrão K8s descrito em Empacotamento do `CLAUDE.md`.

### `AddApiDatabase`/`AddApiCaching` liam a connection string cedo demais

Achado mais sério que o do redirect HTTPS, descoberto investigando a mesma flakiness: `AddApiDatabase` e `AddApiCaching` liam `configuration.GetConnectionString(...)` numa variável local **antes** de registrar `AddDbContext`/`AddStackExchangeRedisCache`. Isso captura o valor no momento em que `Program.cs` executa suas instruções de nível superior — que, sob `WebApplicationFactory`, acontece **antes** do hook `ConfigureWebHost`/`ConfigureAppConfiguration` da fixture de teste terminar de compor a configuração final. Resultado: `AppDbContext`/`IDistributedCache` ficavam presos ao `appsettings.Development.json` (Postgres/Redis reais do `docker-compose`, em `localhost`), nunca aos containers do Testcontainers — mesmo com `IConfiguration.GetConnectionString(...)` já retornando o valor correto de qualquer ponto do código chamado depois do host pronto. Os sintomas eram exatamente o que se esperaria de dados vazando entre execuções: um 409 `book-isbn-duplicate` "impossível" logo na primeira chamada de um teste isolado, porque um teste anterior (rodado minutos ou dias antes) tinha deixado uma linha com aquele ISBN no banco real.

Corrigido lendo a connection string **dentro** do delegate (`options => options.UseNpgsql(configuration.GetConnectionString(...))` / `options => options.Configuration = configuration.GetConnectionString(...)`), que só é invocado na primeira resolução do `DbContextOptions`/`IOptions<RedisCacheOptions>` via DI — depois que o host, e portanto todos os overrides de configuração de teste, já estão prontos. Verificado derrubando os containers reais do `docker-compose` (`docker compose stop postgres redis`) e rodando a suíte de integração 3 vezes seguidas sem falha — só passa se a app estiver mesmo isolada nos containers do Testcontainers.

### Nova linha na tabela Contrato HTTP: `book-isbn-duplicate`

A tabela do `CLAUDE.md` não tinha uma linha para ISBN duplicado em `POST /books` — só cobria os erros de empréstimo e de desativação. `book-isbn-duplicate` (409) segue o mesmo padrão dos demais conflitos da tabela. Tarefa desta change atualizar o `CLAUDE.md` com essa linha, para a tabela continuar sendo a fonte única de verdade do contrato.

## Risks / Trade-offs

- [Eventos de auditoria (`BookCreated`, `BookUpdated`, `BookDeactivated`) não são emitidos] → Aceito como Non-Goal: `Audit` (tabela `audit_events`, `Features/Audit`) não existe. Registrado para a change que introduzir `Audit` revisitar `CreateBook`/`UpdateBook`/`DeactivateBook` e adicionar o evento na mesma `SaveChangesAsync`, como o `CLAUDE.md` exige.
- [`book-has-active-loans` (desativação com empréstimo ativo) não é verificado] → Aceito por decisão do usuário: `DELETE /books/{id}` desta change desativa incondicionalmente. Quando `Loans` existir, essa change precisa adicionar o check antes de desativar — hoje `Loans` não existe, então toda desativação é, por definição, "sem empréstimo ativo".
- [`book-has-history` (exclusão com histórico) nunca dispara] → Observação, não bloqueio: como `DELETE` nunca apaga fisicamente (Proibições do `CLAUDE.md`), não existe caminho de exclusão física para essa regra proteger. A linha da tabela parece descrever um cenário de hard-delete que a arquitetura atual não permite; fica como ponto para o autor do `CLAUDE.md` decidir se remove a linha ou se ela é para uma operação futura.
- [Migração de `LibraryDbContext` para `AppDbContext` toca migrations já commitadas] → Mitigação: só o atributo de contexto no `.Designer.cs` e o nome da classe de snapshot mudam; conteúdo de `Up`/`Down` fica idêntico. `dotnet ef migrations has-pending-model-changes` confirma zero diff de modelo antes de arquivar.

## Migration Plan

1. Mover/renomear arquivos de domínio e persistência (sem lógica nova) — build e suíte de testes existente (23 unitários + 6 integração) devem continuar passando inalterados, só ajustando namespaces/pastas de teste.
2. Adicionar `Testcontainers.Redis` e `Microsoft.AspNetCore.Mvc.Testing`, e a fixture `ApiFixture` (`WebApplicationFactory<Program>` + Postgres/Redis reais).
3. Adicionar `Common/`, `Extensions/CachingExtensions.cs`, `Extensions/ProblemDetailsExtensions.cs`.
4. Adicionar `Features/Books/*` e registrar `BooksEndpoints` no `Program.cs`.
5. Atualizar `CLAUDE.md` com a linha `book-isbn-duplicate` e com a correção sobre onde `DomainException` mora.
6. Sem rollback especial: nenhuma migration de schema nova, e a estrutura de pastas é reversível via `git revert` caso necessário.
