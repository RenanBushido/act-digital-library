## Why

O commit `db70b76` ("refactor: Criação da Pasta Extensions e inserindo os middlewares do projeto") alterou código da capability `catalog-domain` (já arquivada) direto no `main`, sem passar por `/opsx:propose`, sem `design.md` e sem revisão `sdd-review`. Isso viola o processo declarado no `CLAUDE.md` ("Toda change passa por duas revisões... Refatoração oportunista pertence a outra change"). Esta change formaliza retroativamente essa decisão para restaurar a rastreabilidade, e fixa a pasta `Extensions` na estrutura do projeto.

## What Changes

- Extração do registro de `LibraryDbContext` de `Program.cs` para `src/Library.Api/Extensions/DatabaseExtensions.cs` (`AddApiDatabase(this IServiceCollection, IConfiguration)`).
- `LibraryDbContext` migrado para primary constructor (equivalente funcional).
- Mensagens de `DomainException` traduzidas de português para inglês, alinhando com a convenção "Código, identificadores e commits em inglês" do `CLAUDE.md`.
- `CLAUDE.md`: seção Estrutura atualizada para declarar `src/Library.Api/Extensions` como pasta de métodos de registro de serviço (`IServiceCollection`), separada de `Infrastructure` (componentes de runtime).
- Sem mudança de comportamento observável — nenhum requisito de `openspec/specs/catalog-domain/spec.md` é afetado.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
(nenhuma — refactor estrutural, sem mudança de comportamento; `skip_specs: true`)

## Impact

- `src/Library.Api/Extensions/DatabaseExtensions.cs` (novo).
- `src/Library.Api/Program.cs`, `LibraryDbContext.cs`, `Book.cs`, `Isbn.cs`, `Email.cs`, `User.cs`, `GlobalUsings.cs` (já modificados no commit `db70b76`, sem alteração de comportamento).
- `CLAUDE.md` (Estrutura).
- Remoção de `src/Library.Api/Migrations/20260910142846_InitialCreate.{cs,Designer.cs}` — migration vazia e não commitada, gerada por engano (confirmado sem mudanças de modelo pendentes via `dotnet ef migrations has-pending-model-changes`).
