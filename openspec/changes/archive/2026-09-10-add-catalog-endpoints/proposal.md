## Why

`catalog-domain` fixou as entidades `Book`/`User` e o schema, mas não existe nenhum endpoint HTTP ainda. Esta change expõe o catálogo de livros via Minimal API (CRUD de `Book`, sem `Loans`), seguindo o Contrato HTTP e a nova seção "Organização do código" do `CLAUDE.md` — que também exige migrar `Book`/`User`/`LibraryDbContext` para a estrutura `Domain/`/`Infrastructure/Persistence/` antes de construir endpoints em cima deles.

## What Changes

- Migração estrutural (sem mudança de comportamento): `Book`/`Isbn` e `User`/`Email` movidos para `Domain/Book/` e `Domain/User/`; `LibraryDbContext` renomeado para `AppDbContext` e movido, junto com `BookConfiguration`/`UserConfiguration`, para `Infrastructure/Persistence/`.
- Endpoints de catálogo de livros: `GET /books` (lista paginada, cache), `GET /books/{id}`, `GET /books/{id}/availability` (cache), `POST /books`, `PATCH /books/{id}` (título/autor/total de exemplares), `DELETE /books/{id}` (desativação incondicional — sem checar empréstimo ativo, `Loans` ainda não existe).
- Infraestrutura mínima de HTTP compartilhada (`Common/`): `Result<T>`, catálogo de erros de `Book`, correlação (`X-Correlation-Id`), `application/problem+json` com `correlationId`/`traceId`.
- Cache Redis (`IDistributedCache`) só nas duas leituras públicas que o `CLAUDE.md` lista: `GET /books` e `GET /books/{id}/availability`.
- Validação nativa (`AddValidation()`) nos corpos de `POST`/`PATCH /books`.
- Nova entrada na tabela **Contrato HTTP** do `CLAUDE.md`: `book-isbn-duplicate` (409) — não existia uma linha para conflito de ISBN na criação; ver `design.md`.

## Capabilities

### New Capabilities
- `catalog-endpoints`: contrato HTTP de leitura e escrita do catálogo de livros (`Book`), incluindo cache de leitura e as respostas de erro específicas de cada operação.

### Modified Capabilities
(nenhuma — `catalog-domain` descreve o comportamento de `Book`/`User`, que não muda; a migração de pastas é implementação, não comportamento observável)

## Impact

- Estrutura: `src/Library.Api/Domain/{Book,User}/`, `src/Library.Api/Infrastructure/Persistence/`, `src/Library.Api/Common/`, `src/Library.Api/Features/Books/{,Contracts/}`.
- `src/Library.Api/Migrations/`: Designer/snapshot atualizados para `AppDbContext` (sem nova migration de schema — o shape do modelo não muda).
- `tests/Library.UnitTests`: pastas de teste de `Book`/`Isbn`/`User`/`Email` migradas para `Domain/Book`/`Domain/User`, espelhando `src/`.
- `Directory.Packages.props`/`tests/Library.IntegrationTests`: novas dependências `Testcontainers.Redis` e `Microsoft.AspNetCore.Mvc.Testing`, e uma fixture `ApiFixture` (`WebApplicationFactory<Program>` + Postgres/Redis reais) para os testes de endpoint HTTP.
- `CLAUDE.md`: nova linha `book-isbn-duplicate` na tabela Contrato HTTP, e correção da frase da seção Estrutura que ainda atribuía `DomainException` a `Infrastructure` (agora em `Common/`).
- Fora do escopo (registrado como Non-Goal em `design.md`): `Loans`, `Audit` (eventos `BookCreated`/`BookUpdated`/`BookDeactivated` ficam pendentes até a change de Audit existir), métricas OpenTelemetry (`library.loans.*` é de `Loans`), health checks.
