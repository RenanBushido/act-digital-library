## Why

O projeto ainda não tem domínio, persistência ou schema. Antes de expor qualquer endpoint de empréstimo ou catálogo, é preciso modelar `Book` e `User`, mapear no EF Core e gerar a migration inicial — a base sobre a qual as demais changes (endpoints, empréstimos, idempotência, auditoria) vão se apoiar.

## What Changes

- Entidade `Book`: título, ISBN (único, normalizado sem hífens e em maiúsculas antes de comparar), autor, total de exemplares, exemplares disponíveis, ativo, timestamps (`timestamptz`, UTC).
- Entidade `User`: nome, e-mail (único), timestamps (`timestamptz`, UTC).
- `LibraryDbContext` com `IEntityTypeConfiguration<T>` para `Book` e `User`.
- Constraint de banco (`CHECK`) garantindo `available_copies >= 0 AND available_copies <= total_copies`.
- Migration inicial versionada em `src/Library.Api/Migrations`.
- Nenhum endpoint HTTP nesta change — apenas domínio, persistência e schema.

## Capabilities

### New Capabilities
- `catalog-domain`: modelagem das entidades `Book` e `User`, invariantes de construção, unicidade de ISBN e e-mail, e o schema de banco correspondente (constraints, tipos temporais).

### Modified Capabilities
(nenhuma — projeto greenfield, sem specs existentes)

## Impact

- Novo `src/Library.Api/Features/Books/Book.cs`, `Isbn.cs` e `src/Library.Api/Features/Users/User.cs`, `Email.cs` (entidades e value objects com invariantes no construtor).
- Novo `src/Library.Api/Infrastructure/LibraryDbContext.cs`, `DomainException.cs` e configurações de mapeamento (`BookConfiguration`, `UserConfiguration`).
- Nova migration inicial em `src/Library.Api/Migrations`.
- `src/Library.Api/Program.cs`: registro do `LibraryDbContext` com Npgsql; `appsettings.Development.json`: connection string local (sem segredo real).
- `Library.Api.csproj`: dependências EF Core 10 + Npgsql (já previstas no CLAUDE.md).
- `Directory.Packages.props`: nova dependência `Testcontainers.PostgreSql`; pin explícito de `Microsoft.EntityFrameworkCore`/`.Relational` em 10.0.12 com `CentralPackageTransitivePinningEnabled` para resolver conflito de versão transitiva com `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `tests/Library.IntegrationTests/Library.IntegrationTests.csproj`: `ProjectReference` para `Library.Api` e fixture de container Postgres compartilhado (`ICollectionFixture`) para os testes de integração desta change.
- `GlobalUsings.cs` na raiz de `src/Library.Api`, `tests/Library.UnitTests` e `tests/Library.IntegrationTests`, centralizando os `using` de namespace até então declarados por arquivo.
- Sem impacto em contrato HTTP, cache ou observabilidade nesta change.
