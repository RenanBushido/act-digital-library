## Context

O código desta change já existe (commit `db70b76`, aplicado direto no `main` sem passar pelo fluxo SDD). O propósito deste `design.md` é documentar retroativamente o porquê da decisão de estrutura, para não deixar uma divergência silenciosa entre `CLAUDE.md` e o código, e para que a revisão `sdd-review` tenha algo concreto contra o que comparar.

## Goals / Non-Goals

**Goals:**
- Registrar por que `Extensions/` existe como categoria de pasta própria, distinta de `Infrastructure/`.
- Corrigir a estrutura declarada no `CLAUDE.md` para refletir a decisão do autor do projeto.

**Non-Goals:**
- Nenhuma mudança de comportamento, endpoint, ou requisito de `catalog-domain`.
- Não reescreve o histórico do commit `db70b76` — os arquivos já estão como o autor os deixou.

## Decisions

**`Extensions/` vs `Infrastructure/`**: `Infrastructure/` guarda os componentes de runtime em si (`LibraryDbContext`, `DomainException`). `Extensions/` guarda os métodos de extensão de `IServiceCollection` que os conectam ao host (`AddApiDatabase`, e no futuro `AddApiCaching`, `AddApiObservability`, etc.) — um arquivo por área de configuração. Motivação do autor: à medida que mais serviços (Redis, OpenTelemetry, health checks) forem adicionados em changes futuras, `Program.cs` deve continuar sendo uma lista curta de chamadas `builder.Services.AddApiX(...)`, em vez de acumular blocos de configuração inline. Alternativa considerada: colocar os métodos de extensão dentro de `Infrastructure/` junto dos componentes — rejeitada porque mistura "o que o componente é" com "como ele é registrado no DI", e o autor prefere a separação explícita.

**Mensagens de `DomainException` em inglês**: alinhado com "Código, identificadores e commits em inglês" do `CLAUDE.md` — a mensagem da exceção é parte do código, não documentação nem mensagem de commit.

**Migration vazia removida**: `20260910142846_InitialCreate` foi gerada sem que houvesse mudança de modelo pendente (o refactor não alterou o shape das entidades) — confirmado com `dotnet ef migrations has-pending-model-changes`. Mantê-la seria um artefato morto e confuso ao lado da migration real (`20260909234508_InitialCatalogSchema`).

## Risks / Trade-offs

- [Código já aplicado antes da revisão] → Aceito como exceção pontual: esta change documenta a decisão a posteriori. Fica registrado como lembrete de processo: novas alterações em capabilities já arquivadas devem abrir uma change antes de tocar o código, não depois.
