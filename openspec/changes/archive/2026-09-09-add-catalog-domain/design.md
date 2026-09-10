## Context

Projeto greenfield: `src/Library.Api` só tem o esqueleto do host (`Program.cs`, `appsettings.json`). Não há `DbContext`, entidades nem migrations. Esta change fixa a base de domínio e persistência sobre a qual empréstimos, idempotência e auditoria (changes futuras) vão se apoiar. Ver `proposal.md` - Why.

Restrições do CLAUDE.md relevantes aqui: nenhuma garantia de correção pode depender de estado em processo; `available_copies` é contador, nunca projeção; proibido `rowversion`; `TimeProvider` injetado, nunca `DateTime.UtcNow` direto; todas as colunas temporais `timestamptz` UTC; migrations em `src/Library.Api/Migrations`.

## Goals / Non-Goals

**Goals:**
- Modelar `Book` e `User` com invariantes de construção que impeçam estados inválidos em memória.
- Garantir unicidade de ISBN (normalizado) e e-mail no banco, não só na aplicação.
- Fixar a constraint `CHECK (available_copies >= 0 AND available_copies <= total_copies)` na migration inicial.
- Gerar a migration inicial versionada e compilável.

**Non-Goals:**
- Nenhum endpoint HTTP, `Result<T>`, `ProblemDetails` ou validação de borda — isso é de uma change futura sobre contrato HTTP.
- Nenhuma lógica de empréstimo, idempotência ou auditoria.
- Nenhuma configuração de cache Redis.
- Nenhum teste de concorrência (`ExecuteUpdateAsync` condicional) — a constraint de banco é a única garantia fixada aqui; a lógica condicional de decremento pertence à change de empréstimos.

## Decisions

**Estrutura de pastas**: `src/Library.Api/Features/Books/Book.cs` e `BookConfiguration.cs`; `src/Library.Api/Features/Users/User.cs` e `UserConfiguration.cs`; `src/Library.Api/Infrastructure/LibraryDbContext.cs`. Segue a convenção do CLAUDE.md (pastas por feature, sem camadas Application/Domain separadas).

**Invariantes no construtor**: `Book` e `User` expõem construtores privados/protegidos e um factory method (`Book.Create(...)`) que valida e lança exceção de domínio em caso de violação (total de exemplares > 0, available ≤ total, título não vazio, ISBN não vazio, autor não vazio, e-mail não vazio). Alternativa considerada: validar só via Data Annotations no EF — rejeitada porque o CLAUDE.md exige invariantes no construtor da entidade, não na borda de persistência.

**Normalização de ISBN**: método estático `Isbn.Normalize(string)` (remove hífens, upper-case) aplicado antes de comparar ou persistir. A coluna armazena o valor normalizado; não guardamos o valor original digitado, pois o requisito é unicidade sobre o valor normalizado e não há requisito de exibir o formato original.

**Unicidade no banco**: índice único (`CREATE UNIQUE INDEX`) sobre `books.isbn` (já normalizado) e sobre `users.email` (normalizado para lowercase antes de persistir, mesma lógica de normalização). A aplicação não pode ser a única barreira porque replica em 2 a 11 instâncias.

**Normalização de e-mail**: assim como `Isbn`, um método estático `Email.Normalize(string)` (trim, lower-case) aplicado antes de comparar ou persistir, garantindo o mesmo tratamento simétrico dado ao ISBN em vez de deixar a normalização implícita na configuração do EF.

**Timestamps**: `CreatedAtUtc` e `UpdatedAtUtc` como `timestamptz`, preenchidos via `TimeProvider.GetUtcNow()` injetado no ponto de criação/atualização (não no `DbContext.SaveChanges` para manter a decisão explícita na entidade, consistente com "invariantes no construtor").

**Migration**: gerada com `dotnet ef migrations add InitialCatalogSchema` apontando para `src/Library.Api/Migrations`, revisada manualmente para confirmar a constraint `CHECK` e os índices únicos (o EF Core não gera `CHECK` a partir de anotações simples; será adicionada via `HasCheckConstraint` na configuração do `Book`).

## Risks / Trade-offs

- [Normalização de ISBN perde o formato original] → Aceito: não há requisito de exibição do ISBN formatado nesta change; se necessário, uma change futura adiciona campo de exibição sem quebrar a unicidade.
- [Invariantes de domínio duplicadas entre construtor e `CHECK` de banco] → Intencional: a constraint é a rede de segurança independente da lógica de aplicação, conforme CLAUDE.md ("nunca a remova para simplificar").
- [Migration gerada automaticamente pode não incluir a `CHECK` constraint] → Mitigação: revisão manual obrigatória do arquivo de migration antes de commitar, adicionando `migrationBuilder.Sql(...)` ou `HasCheckConstraint` se o EF não gerar.
- [`Isbn`/`Email` como `readonly record struct` têm um construtor sem parâmetros implícito do próprio C# (`default(Isbn)`, `new Isbn()`), que produz `Value == null` sem passar por `Create`/`Normalize`] → Aceito por ora: nada no código atual usa `default`/`new()` diretamente, e o EF Core sempre lê via `Isbn.Create(value)`/`Email.Create(value)` no conversor. Fica registrado como cuidado para changes futuras que manipulem essas entidades: não usar `default(Isbn)` ou `default(Email)` como valor-sentinela.
