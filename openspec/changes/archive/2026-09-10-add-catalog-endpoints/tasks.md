## 1. Migração de estrutura (sem mudança de comportamento)

- [x] 1.1 Mover `Book.cs`/`Isbn.cs` para `src/Library.Api/Domain/Book/` e `User.cs`/`Email.cs` para `src/Library.Api/Domain/User/`, ajustando namespaces; `dotnet build` sem warnings.
- [x] 1.2 Renomear `LibraryDbContext` para `AppDbContext`, mover para `src/Library.Api/Infrastructure/Persistence/AppDbContext.cs`; mover `BookConfiguration.cs`/`UserConfiguration.cs` para a mesma pasta.
- [x] 1.3 Atualizar `Program.cs`, `DatabaseExtensions.cs` e `GlobalUsings.cs` (nos 3 projetos) para os novos namespaces/nome de classe.
- [x] 1.4 Atualizar `[DbContext(typeof(LibraryDbContext))]` para `typeof(AppDbContext)` tanto no `.Designer.cs` da migration existente quanto dentro de `LibraryDbContextModelSnapshot.cs`, e renomear a classe/arquivo do snapshot para `AppDbContextModelSnapshot`; verificar com `dotnet ef migrations has-pending-model-changes` que não há diff de modelo.
- [x] 1.5 Mover `tests/Library.UnitTests/Features/Books/{BookTests,IsbnTests}.cs` para `tests/Library.UnitTests/Domain/BookTests/` e `tests/Library.UnitTests/Features/Users/{UserTests,EmailTests}.cs` para `tests/Library.UnitTests/Domain/UserTests/` (sufixo `Tests` no namespace/pasta folha para evitar colisão do compilador entre o namespace `...Domain.Book` e o tipo `Book` importado via `global using`), espelhando a nova estrutura de `src/`.
- [x] 1.6 Rodar a suíte completa (23 unitários + 6 integração) e confirmar que todos continuam passando sem alteração de asserts, só de `using`/namespace.

## 2. Infraestrutura de testes de integração HTTP

- [x] 2.1 Adicionar `Testcontainers.Redis` ao grupo `Database` em `Directory.Packages.props` e referenciá-lo em `tests/Library.IntegrationTests/Library.IntegrationTests.csproj`.
- [x] 2.2 Adicionar `Microsoft.AspNetCore.Mvc.Testing` (para `WebApplicationFactory<Program>`) a `Directory.Packages.props`/`Library.IntegrationTests.csproj`.
- [x] 2.3 Criar `ApiFixture` (`WebApplicationFactory<Program>`) em `Library.IntegrationTests/Infrastructure`, subindo Postgres e Redis via Testcontainers (reaproveitando o container Postgres já usado por `PostgresFixture` ou substituindo-a), sobrescrevendo as connection strings da app via `WithWebHostBuilder`; `ICollectionFixture` compartilhado para toda a coleção de testes de endpoint, com limpeza de dados entre testes.
- [x] 2.4 Teste smoke: `GET /books` contra a `ApiFixture` retorna 200 com lista vazia num banco limpo, confirmando que a app sobe corretamente com Postgres e Redis reais. (Só executável depois da task 4.8, quando `GET /books` existir — a fixture em si já foi criada na 2.3; o teste escrito e verificado junto da 4.8.)

## 3. Infraestrutura compartilhada (Common/Extensions)

- [x] 3.1 Implementar `Common/DomainException.cs` (movido) e `Common/Errors/Error.cs` com `Common/Errors/ErrorHttpResultExtensions.cs` (`Error.ToProblem()`) — decisão revisada em `design.md`: sem `Result`/`Result<T>` intermediário, que ficou sem consumidor.
- [x] 3.2 Implementar `Common/Errors/BookErrors.cs` com `NotFound`, `Inactive`, `IsbnDuplicate`, `InsufficientAvailableCopies`, `ValidationFailed`, cada um mapeando para o status/`type` da tabela Contrato HTTP.
- [x] 3.3 Implementar `Common/CorrelationIdMiddleware.cs`: lê `X-Correlation-Id` do cliente ou gera um, adiciona ao escopo de log e ao header de resposta; teste unitário cobrindo os dois casos (eco e geração), satisfazendo o requisito "Correlation id is echoed on every response".
- [x] 3.4 Implementar `Extensions/ProblemDetailsExtensions.cs` (`AddApiProblemDetails`) customizando `CustomizeProblemDetails` para incluir `correlationId`/`traceId`; registrar em `Program.cs`.
- [x] 3.5 Implementar `Extensions/CachingExtensions.cs` (`AddApiCaching`) registrando `IDistributedCache` via `AddStackExchangeRedisCache`, lendo a connection string de configuração; registrar em `Program.cs`.
- [x] 3.6 Implementar `Common/Pagination/PagedResult.cs`.
- [x] 3.7 Teste de integração (via `ApiFixture`) verificando que uma resposta de erro real (ex.: `GET /books/{id}` com id inexistente) é `application/problem+json` com a extensão `correlationId` igual ao `X-Correlation-Id` enviado pelo cliente, e com `correlationId` gerado quando o header está ausente — cobrindo os dois cenários do requisito "Error responses are Problem Details with correlation and trace identifiers".
- [x] 3.8 Teste de integração (via `ApiFixture`) verificando que uma resposta de sucesso real (ex.: `GET /books` 200) também ecoa o header `X-Correlation-Id` do cliente e gera um quando ausente — cobrindo o requisito "Correlation id is echoed on every response" no caminho feliz.

## 4. Casos de uso de Book

Cada handler abaixo recebe `CancellationToken` do parâmetro do endpoint e o propaga até a última chamada de `AppDbContext`/`IDistributedCache`, conforme a convenção do `CLAUDE.md`.

- [x] 4.1 Implementar `Features/Books/Contracts/BookResponse.cs`, `BookAvailabilityResponse.cs`, `PagedBookResponse.cs` com `From(entidade)`.
- [x] 4.2 Implementar `Features/Books/CreateBook.cs` (request, validação, handler `static`, `CancellationToken` propagado) usando `Book.Create` e retornando `IsbnDuplicate` em violação de unicidade; teste de integração cobrindo criação válida, corpo inválido e ISBN duplicado.
- [x] 4.3 Implementar `Features/Books/GetBook.cs` (`CancellationToken` propagado); teste de integração cobrindo livro existente (ativo e inativo) e `book-not-found`.
- [x] 4.4 Implementar `Features/Books/ListBooks.cs` (`CancellationToken` propagado) com paginação (`page`, `pageSize`), **excluindo livros inativos por padrão** (`is_active == true`), e leitura via cache (`Common/Pagination/PagedResult`); teste de integração cobrindo lista paginada, livro inativo ausente da lista, e invalidação de cache após `POST /books`.
- [x] 4.5 Implementar `Features/Books/GetBookAvailability.cs` (`CancellationToken` propagado) com leitura via cache; teste de integração cobrindo disponibilidade existente (ativo e inativo) e `book-not-found`.
- [x] 4.6 Implementar `Features/Books/UpdateBook.cs` (`CancellationToken` propagado): título/autor/total de exemplares, aplicando o delta em `available_copies` e rejeitando com `InsufficientAvailableCopies` quando o resultado for negativo, e com `Inactive` quando o livro estiver desativado; teste de integração cobrindo os três casos e o `book-not-found`.
- [x] 4.7 Implementar `Features/Books/DeactivateBook.cs` (`CancellationToken` propagado): desativação idempotente (`is_active = false`), sem checar empréstimos; teste de integração cobrindo livro ativo, já inativo e `book-not-found`.
- [x] 4.8 Implementar `Features/Books/BooksEndpoints.cs` (`MapGroup("/books")`, `AddValidation()` nos endpoints de escrita) e registrar em `Program.cs`.

## 5. Cache — cenários de degradação

- [x] 5.1 Teste de integração verificando que `GET /books` e `GET /books/{id}/availability` continuam respondendo 200 quando o Redis está indisponível (falha logada como warning, sem propagar erro ao cliente).
- [x] 5.2 Teste de integração verificando que o cache de `GET /books` é invalidado (`RemoveAsync` depois do `CommitAsync`) após `POST /books`.

## 6. Documentação e verificação final

- [x] 6.1 Adicionar a linha `book-isbn-duplicate` (409) na tabela Contrato HTTP do `CLAUDE.md`.
- [x] 6.2 Corrigir `CLAUDE.md` (seção Estrutura, linha sobre `Infrastructure`/`DomainException`) para refletir a decisão desta change de que `DomainException` mora em `Common/`, não em `Infrastructure`.
- [x] 6.4 Remover `app.UseHttpsRedirection()` de `Program.cs`: com TLS terminado fora do processo (`docker-compose.yml` só expõe `ASPNETCORE_HTTP_PORT`), o middleware causava redirect em toda requisição e o `HttpClient` reenviava `POST`/`PATCH`/`DELETE` duplicado; `ApiFixture` (`ConfigureClient` com `BaseAddress = https://localhost`) mantido como rede de segurança contra uma reintrodução futura do middleware.
- [x] 6.5 Corrigir `AddApiDatabase`/`AddApiCaching` para ler a connection string dentro do delegate de configuração, não numa variável capturada antes — a captura antecipada fazia a app (inclusive sob `WebApplicationFactory`) sempre se conectar ao Postgres/Redis reais do `docker-compose` em vez dos containers do Testcontainers, mascarado até então porque os dois "funcionavam" apontando pro mesmo lugar. Verificado derrubando `docker compose stop postgres redis` e rodando a suíte de integração 3x sem falha.
- [x] 6.3 `dotnet build` nos 3 projetos sem warnings; `dotnet test` unitários e de integração passando (repetido 3x com o Postgres/Redis reais do `docker-compose` parados, para provar isolamento de verdade); `openspec validate add-catalog-endpoints --strict` válido.
