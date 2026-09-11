## Context

Ver `proposal.md` - Why. Estado atual relevante para o desenho:

- `Program.cs` hoje nunca aplica migration: não existe `--migrate-only`, não existe checagem de flag, e o `WebApplicationFactory<Program>` dos testes de integração migra "por fora" (`ApiFixture.InitializeAsync` chama `dbContext.Database.MigrateAsync()` diretamente) - a aplicação em si nunca foi exercitada nesse caminho.
- `Program` já é `partial class Program;` só para o `WebApplicationFactory<Program>` dos testes existirem; não há hoje nenhum ponto de entrada testável além do host inteiro.
- `docker-compose.yml` já existe com `postgres`, `redis`, `otel` (Aspire Dashboard como coletor OTLP) e os serviços `migrator`/`api` sob o profile `app`, ambos com `build: dockerfile: src/Library.Api/Dockerfile` - um Dockerfile que ainda não existe. O `api` depende de `migrator: condition: service_completed_successfully`, e do `postgres`/`redis` saudáveis - a cadeia de dependência já está desenhada, só falta a imagem e o `--migrate-only` que ela invoca.
- O serviço `api` do compose atual define `ASPNETCORE_ENVIRONMENT: "Development"`. Isso é inconsistente com a decisão desta change (flag de config `Database:MigrateOnStartup`, não nome de ambiente) porque, se essa flag for `true` só em `appsettings.Development.json` e a imagem publicada incluir esse arquivo, rodar em `Development` dentro do contêiner ligaria a migration automática mesmo em Docker - o oposto do que o CLAUDE.md fixa. Corrigir isso é escopo desta change ("Ajustes no docker-compose.yml necessários para subir o profile app").
- `Extensions/DatabaseExtensions.cs` registra `AddDbContext<AppDbContext>` com `UseNpgsql(connectionString)` sem nenhum ajuste de pool.
- `Extensions/ObservabilityExtensions.cs` já registra `/health/live` e `/health/ready` (change `add-observability`, arquivada) - esta change só aponta os probes do Kubernetes para eles, sem alterar seu contrato.
- Não existe `k8s/` nem `README.md` no repositório ainda.

## Goals / Non-Goals

**Goals:**
- Tornar `--migrate-only` um modo real do `Program.cs`, testável sem depender de subir um processo separado.
- Decidir a aplicação de migration por uma flag de configuração (`Database:MigrateOnStartup`), nunca pelo nome do ambiente.
- Empacotar a aplicação numa imagem mínima, não privilegiada, compatível com o `docker-compose.yml` e com os manifests Kubernetes.
- Corrigir o `docker-compose.yml` para que `--profile app up --build` suba o ambiente inteiro corretamente, com a API só respondendo depois da migration.
- Entregar manifests Kubernetes mínimos e documentar os dois cenários de verificação manual no `README.md`.

**Non-Goals:**
- Não criar Helm chart nem Kustomize overlays.
- Não adicionar pipeline de CI, autoscaling (HPA), Ingress ou NetworkPolicy.
- Não provisionar o cluster Kubernetes em si (kind, minikube, cloud) - os manifests assumem um cluster já existente.
- Não mudar o contrato de `/health/live` e `/health/ready` fixado em `observability` - só reaponta os probes para eles.

## Decisions

### `--migrate-only` como método testável, não só `Environment.Exit` em `Main`

`Program.cs` ganha um método `internal static async Task<int> RunMigrateOnlyAsync(string[] args)` que constrói um host mínimo (só o suficiente para resolver `AppDbContext`), chama `Database.MigrateAsync()`, e devolve `0` em sucesso. Uma exceção durante a migration é capturada, logada, e o método devolve `1`. O fluxo normal de `Main` passa a ser:

```
if (args.Contains("--migrate-only"))
{
    return await Program.RunMigrateOnlyAsync(args);
}
```

com `Main` só then chamando `Environment.Exit` do lado de fora (ou o `dotnet run`/entrypoint do contêiner propagando o código de saída do próprio `Main`, já que top-level statements com `return` já definem o exit code do processo).

**Por que não testar via subprocesso (`Process.Start` no `.dll` publicado):** os cenários 1 e 2 do enunciado ("--migrate-only aplica e sai com 0" / "--migrate-only com banco inacessível sai com código diferente de 0") pedem "testável em `dotnet test`". Rodar o binário publicado como processo filho funcionaria, mas duplicaria toda a infraestrutura de Testcontainers só para reobter, via `Process.ExitCode`, uma informação que o próprio método já devolve como `int`. Expor `RunMigrateOnlyAsync` como método chamável diretamente do teste (à semelhança do que `WebApplicationFactory<Program>` já faz com a classe `Program` inteira) mantém o teste rápido e determinístico, sem gerenciar um processo externo.

### `Database:MigrateOnStartup` lido em `Program.cs`, aplicado antes de `app.Run()`

Uma nova seção `Database` em `appsettings.json` (`MigrateOnStartup: false` por padrão) e `appsettings.Development.json` (`MigrateOnStartup: true`). Em `Program.cs`, depois de `app.Build()` e antes de `app.Run()`:

```
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}
```

Isso substitui a checagem por `app.Environment.IsDevelopment()` que o CLAUDE.md antigo descrevia (a aplicação nunca chegou a implementar essa versão) - a decisão agora é só configuração, então Docker/Kubernetes controlam via variável de ambiente (`Database__MigrateOnStartup=false`, o padrão) sem depender de `ASPNETCORE_ENVIRONMENT`.

**Alternativa descartada - continuar checando `ASPNETCORE_ENVIRONMENT`:** é exatamente o problema que o `docker-compose.yml` atual expõe (serviço `api` roda com `ASPNETCORE_ENVIRONMENT=Development`, então herdaria `appsettings.Development.json` mesmo em contêiner). Uma flag dedicada separa a decisão "que ambiente é este" de "quem aplica migration", permitindo, por exemplo, rodar um `Development` local sem Docker (com `dotnet run`) migrando sozinho, mas o mesmo `appsettings.Development.json` nunca ser usado dentro da imagem publicada em Docker/Kubernetes (que não define `ASPNETCORE_ENVIRONMENT=Development`).

### Pool de conexões fixado em código, não deixado para cada connection string

`Extensions/DatabaseExtensions.cs` envolve a connection string configurada com `NpgsqlConnectionStringBuilder` e fixa `MaxPoolSize = 8` antes de passar para `UseNpgsql`, em vez de exigir que cada ambiente (local, Docker, Kubernetes) inclua `Maximum Pool Size=8` manualmente na sua própria `ConnectionStrings:Postgres`. Isso elimina a chance de uma réplica subir com o pool default do Npgsql (100) e a conta de 11 réplicas do CLAUDE.md deixar de valer silenciosamente porque alguém esqueceu o parâmetro numa connection string.

**A conta com 11 réplicas:** `Maximum Pool Size=8` × 11 réplicas = 88 conexões simultâneas no pior caso (todas as réplicas com o pool cheio ao mesmo tempo). O `max_connections` padrão do PostgreSQL é 100, então 88 deixa 12 conexões de folga - suficiente para o `migrator` (uma conexão, de vida curta, e nunca concorrente com as réplicas da aplicação já que o `Job` do migrator conclui antes de elas subirem) e para acesso administrativo/`psql` manual durante operação. Números maiores de pool (16, 32) estourariam esse limite antes de chegar a 11 réplicas; um valor menor (4) reduziria a folga de concorrência dentro de cada réplica sem necessidade, já que o volume de escrita por requisição já é baixo (poucas queries por chamada, ver `CreateLoan.HandleAsync`). `PgBouncer` resolveria o problema de forma mais geral (pooling compartilhado entre réplicas), mas é infraestrutura adicional fora do escopo desta change - registrado como evolução, não implementado.

### Dockerfile multi-stage, imagem final chiseled

Três estágios: `build` (SDK, restore em camada própria antes de copiar o restante do código-fonte, para cache de camada do Docker sobreviver a mudanças de código sem mudança de dependências), `publish` (`dotnet publish` sem SDK extra), e a imagem final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` copiando só a saída de publish, com `USER $APP_UID`. Porta HTTP via `ASPNETCORE_HTTP_PORTS` (variável de ambiente nativa do .NET 8+, não uma linha de código) - a mesma abordagem que o `docker-compose.yml` já usa hoje (`ASPNETCORE_HTTP_PORT: "8080"`).

**Por que a imagem chiseled não recebe `healthcheck` no Compose:** a variante `-chiseled` não tem shell (`/bin/sh`) nem `curl`/`wget` - a forma usual de `healthcheck` no Compose (`CMD-SHELL "curl ..."`) falha silenciosamente porque não há shell para interpretar o comando, e o container aparentaria saudável indefinidamente (ou "starting" para sempre) sem sinalizar o problema real. A alternativa `HEALTHCHECK CMD ["/app/Library.Api", "--health-check"]` exigiria adicionar um modo de healthcheck ao próprio binário só para o Compose, o que está fora do escopo pedido (`Fora de escopo` já exclui trabalho não pedido no enunciado) - o Compose local, portanto, fica sem `healthcheck` no `api`; a checagem de saúde real é feita pelos probes do Kubernetes (`livenessProbe`/`readinessProbe` com `httpGet`, executados pelo `kubelet`, que fala HTTP diretamente sem depender de shell dentro do contêiner) e, localmente, pelo próprio `depends_on: condition: service_completed_successfully` no `migrator` mais a checagem manual documentada no README.

**Alternativas descartadas:**
- `dotnet publish /t:PublishContainer` (SDK Container Tools): gera a imagem sem Dockerfile explícito, mas dificulta controlar os três estágios (cache de restore separado, `USER $APP_UID` explícito) e o pipeline de CI declarado como fora de escopo desta change tornaria isso conveniente sem necessidade agora; um Dockerfile explícito é mais direto de revisar e não exige aprender uma convenção adicional do SDK.
- Imagem Alpine (`aspnet:10.0-alpine`): menor que a `noble` não-chiseled, mas ainda tem um shell completo e gerenciador de pacotes - não atinge a superfície mínima que `chiseled` oferece (sem shell, sem gerenciador de pacotes, sem utilitários), que é o que o CLAUDE.md fixa.
- AOT nativo (`PublishAot`): eliminaria o runtime do .NET da imagem, mas exige garantir compatibilidade AOT de toda a árvore de dependências (Npgsql, StackExchange.Redis, OpenTelemetry, EF Core em modo AOT tem suporte parcial) - risco desproporcional ao ganho para este desafio, e o enunciado não pede.

### Kubernetes: `Job` para o migrator, não initContainer

Um `Job` dedicado roda a mesma imagem com `command: ["--migrate-only"]`, aplicado antes do `Deployment` da API (ordem documentada no README: `kubectl apply -f k8s/migrator-job.yaml` aguardando `kubectl wait --for=condition=complete job/migrator`, depois o `Deployment`). Não é um initContainer porque um initContainer roda **por pod**: com `replicas: N`, a migration seria disparada N vezes em paralelo a cada rollout (mesmo sendo idempotente, é uma corrida desnecessária contra o próprio schema, e multiplica o custo de cada rollout por N execuções da mesma migration). Um `Job` roda uma única vez, independente de quantas réplicas o `Deployment` declarar depois.

### Composição no `docker-compose.yml`

Ajustes ao arquivo existente, não uma reescrita: remover `ASPNETCORE_ENVIRONMENT: "Development"` do serviço `api` (motivo acima), remover qualquer `healthcheck` `CMD-SHELL` que viesse a ser adicionado ao `api` (não existe hoje, mas o Dockerfile chiseled torna isso uma armadilha fácil de reintroduzir por engano), e confirmar que `Database__MigrateOnStartup` não precisa ser setado explicitamente no `api` do compose (o padrão do `appsettings.json` já é `false`, e sem `ASPNETCORE_ENVIRONMENT=Development` o `appsettings.Development.json` nunca é carregado dentro do contêiner).

**`healthcheck` do `postgres` também estava quebrado, bloqueando o profile `app` por completo:** `test: ["CMD", "pg_isready -U library -d library"]` passa a string inteira como um único argumento (forma exec do Compose não faz `shell splitting`); o Docker tentava executar um binário literalmente chamado `pg_isready -U library -d library`, inexistente, e o Postgres nunca era marcado `healthy` - `migrator`/`api`, que dependem de `postgres: condition: service_healthy`, nunca chegavam a iniciar. Corrigido para `["CMD", "pg_isready", "-U", "library", "-d", "library"]` (um argumento por elemento, no mesmo padrão já usado no `healthcheck` do `redis`). Confirmado em teste manual: com o bug, `docker compose --profile app up --build` travava com `postgres` eternamente "unhealthy"; corrigido, a subida completa (cenário 4) e a falha de migration impedindo a API de subir (cenário 5, testada trocando a senha do `migrator` por uma incorreta) se comportaram exatamente como a spec exige.

**Duas variáveis com nome errado encontradas no arquivo existente, corrigidas na mesma passada:** `ASPNETCORE_HTTP_PORT` (singular) não é reconhecida pelo runtime - a variável real é `ASPNETCORE_HTTP_PORTS` (plural); o app continuava respondendo em 8080 só porque a própria imagem base do .NET 8+ já usa 8080 como porta default embutida, mascarando o erro. `OTEL_EXPORTER__OTLP__ENDPOINT` (com `__`, notação de seção do `IConfiguration` do .NET) também não é o nome que o OpenTelemetry SDK lê - a variável padrão OTel é `OTEL_EXPORTER_OTLP_ENDPOINT` (underscore simples, é uma env var lida diretamente pelo SDK, não uma chave de configuração do .NET); sem a correção, o exportador cairia no endpoint default (`http://localhost:4317`, dentro do próprio contêiner da API, onde nada escuta) e traces/métricas seriam descartados em silêncio - sem quebrar a resposta HTTP, mas esvaziando o propósito do serviço `otel` do compose.

## Risks / Trade-offs

- [`RunMigrateOnlyAsync` constrói um host reduzido separado do host completo da aplicação] → mitigação: reaproveita `AddApiDatabase` (a mesma extensão usada pelo host completo), então a connection string e o pool são configurados de forma idêntica; só omite os registros que não servem para migration (observability, caching, endpoints).
- [Testar `--migrate-only` chamando o método diretamente, não o binário publicado] → mitigação: o teste ainda cobre o comportamento real (mesma extensão de DB, mesmo `MigrateAsync`); o que fica fora da cobertura automatizada é só a análise de argumentos da linha de comando em si (`args.Contains("--migrate-only")`), testada implicitamente pelos cenários manuais do README (subida via Compose já invoca o binário publicado com esse argumento).
- [Pool de conexões fixo em código (8) em vez de configurável por ambiente] → aceitável: o número vem de uma conta específica para o teto de réplicas deste desafio (11); se o teto mudar, o valor precisa ser revisitado de qualquer forma, e um valor fixo evita divergência silenciosa entre ambientes.
- [Sem CI, os manifests Kubernetes e o Dockerfile não são validados automaticamente a cada mudança] → declarado como fora de escopo; a verificação fica manual, documentada no README (cenários 4 e 5).
- [A imagem chiseled não tem `libgssapi_krb5.so.2`; o Npgsql emite um aviso (`Cannot load library libgssapi_krb5.so.2`) ao conectar] → confirmado inofensivo em teste manual: o Npgsql tenta carregar a lib para negociação GSSAPI/Kerberos, não a encontra, e cai de volta para autenticação por senha (a usada aqui) sem falhar a conexão nem a migration.

## Migration Plan

Sem migration de dado. Passos de adoção: build da imagem (`docker compose --profile app build`), validação local completa (`docker compose --profile app up --build`), depois aplicação dos manifests Kubernetes na ordem documentada (migrator Job → aguardar conclusão → Deployment/Service/ConfigMap/Secret). Rollback é reverter para a imagem/manifests anteriores; como o modo `--migrate-only` só aplica migrations do EF Core (já com estratégia de banco fixada no CLAUDE.md), não há passo de rollback de schema específico desta change.
