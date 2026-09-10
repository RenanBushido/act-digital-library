## 1. Dependências e projeto

- [x] 1.1 Confirmar em `Directory.Packages.props` a versão fixada de `Npgsql.EntityFrameworkCore.PostgreSQL` (já referenciado em `Library.Api.csproj`) e verificar que `dotnet build` compila sem warnings.
- [x] 1.2 Criar as pastas `src/Library.Api/Features/Books`, `src/Library.Api/Features/Users` e `src/Library.Api/Infrastructure` e verificar que existem no filesystem.
- [x] 1.3 Adicionar o pacote `Testcontainers.PostgreSql` ao grupo `Database` em `Directory.Packages.props` e referenciá-lo em `tests/Library.IntegrationTests/Library.IntegrationTests.csproj`.
- [x] 1.4 Criar a fixture de container compartilhado (`PostgresFixture` + `ICollectionFixture`) em `Library.IntegrationTests`, subindo um único container Postgres para toda a coleção de testes, com limpeza de dados entre testes.
- [x] 1.5 Criar `GlobalUsings.cs` na raiz de `src/Library.Api`, `tests/Library.UnitTests` e `tests/Library.IntegrationTests`, movendo os `using` de namespace declarados por arquivo para lá (requisito do brief `docs/add-catalog-domain.md`).

## 2. Entidade Book

- [x] 2.1 Implementar `Isbn` (normalização: remover hífens, upper-case) com teste unitário cobrindo `978-3-16-148410-0` e `9783161484100` normalizando para o mesmo valor.
- [x] 2.2 Implementar `Book` com construtor/factory validando total de exemplares > 0, `AvailableCopies <= TotalCopies`, título, ISBN e autor não vazios; teste unitário cobrindo criação válida e as rejeições (total zero/negativo, available > total, título vazio, ISBN vazio, autor vazio).
- [x] 2.3 Implementar `BookConfiguration : IEntityTypeConfiguration<Book>` mapeando colunas, índice único em `isbn`, `CHECK (available_copies >= 0 AND available_copies <= total_copies)` e timestamps como `timestamptz`.

## 3. Entidade User

- [x] 3.1 Implementar `Email` (normalização: trim e lower-case) com teste unitário cobrindo `Leitor@Example.com` e `leitor@example.com` normalizando para o mesmo valor, e `" leitor@example.com "` (com espaços) normalizando para o mesmo valor sem espaços.
- [x] 3.2 Implementar `User` com construtor/factory validando nome e e-mail não vazios; teste unitário cobrindo criação válida e as rejeições (nome vazio, e-mail vazio).
- [x] 3.3 Implementar `UserConfiguration : IEntityTypeConfiguration<User>` mapeando colunas, índice único em `email` (normalizado lowercase) e timestamps como `timestamptz`.

## 4. DbContext e migration

- [x] 4.1 Implementar `LibraryDbContext` com `DbSet<Book>` e `DbSet<User>`, aplicando as configurações via `ApplyConfigurationsFromAssembly`.
- [x] 4.2 Registrar `LibraryDbContext` no `Program.cs` com Npgsql, lendo a connection string de configuração (sem segredo hardcoded).
- [x] 4.3 A partir do diretório `src/Library.Api`, gerar a migration inicial com `dotnet ef migrations add InitialCatalogSchema -o Migrations` (garantindo que caia em `src/Library.Api/Migrations`) e revisar manualmente o arquivo gerado, confirmando que a constraint `CHECK` e os dois índices únicos estão presentes.
- [x] 4.4 Verificar que `dotnet ef database update` aplica a migration com sucesso contra um Postgres local (via `docker-compose.yml`).

## 5. Testes de integração

- [x] 5.1 Teste de integração (Testcontainers + Postgres real) verificando que inserir dois `Book` com ISBN equivalente após normalização causa violação de unicidade no banco.
- [x] 5.2 Teste de integração verificando que uma escrita direta violando `available_copies >= 0 AND available_copies <= total_copies` é rejeitada pelo Postgres.
- [x] 5.3 Teste de integração verificando que inserir dois `User` com o mesmo e-mail, ou com e-mails diferindo só por caixa (`Leitor@Example.com` vs `leitor@example.com`), causa violação de unicidade no banco.
- [x] 5.4 Teste de integração verificando que os timestamps persistidos são `timestamptz` em UTC.
- [x] 5.5 Teste de integração verificando que inserir dois `Book` com ISBNs distintos é aceito e ambos persistem.
