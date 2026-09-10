## 1. Estrutura e documentação

- [x] 1.1 Atualizar `CLAUDE.md` (seção Estrutura) para declarar `src/Library.Api/Extensions` como pasta de métodos de registro de serviço, distinta de `Infrastructure`.
- [x] 1.2 Confirmar que `src/Library.Api/Extensions/DatabaseExtensions.cs` segue a convenção declarada (um arquivo por área de configuração, método `AddApiX(this IServiceCollection, ...)`).

## 2. Limpeza

- [x] 2.1 Remover `src/Library.Api/Migrations/20260910142846_InitialCreate.cs` e `.Designer.cs` (migration vazia, não commitada).
- [x] 2.2 Confirmar via `dotnet ef migrations has-pending-model-changes` que não há mudança de modelo pendente após a remoção.

## 3. Verificação

- [x] 3.1 `dotnet build` nos 3 projetos sem warnings.
- [x] 3.2 `dotnet test` unitários e de integração passando.
