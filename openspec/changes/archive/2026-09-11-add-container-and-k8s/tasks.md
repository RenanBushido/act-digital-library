## 1. Modo `--migrate-only` e migration no startup

- [x] 1.1 Adicionar `Database:MigrateOnStartup` (bool, padrão `false`) a `appsettings.json`, e `MigrateOnStartup: true` a `appsettings.Development.json`.
- [x] 1.2 Em `Extensions/DatabaseExtensions.cs`, envolver a connection string configurada com `NpgsqlConnectionStringBuilder` fixando `MaxPoolSize = 8` antes de `UseNpgsql`; verificar que a aplicação continua subindo localmente com `dotnet run`.
- [x] 1.3 Adicionar `internal static async Task<int> RunMigrateOnlyAsync(string[] args)` em `Program.cs`: constrói um host mínimo com `AddApiDatabase`, chama `AppDbContext.Database.MigrateAsync()`, devolve `0` em sucesso; captura exceção, loga, devolve `1`.
- [x] 1.4 Em `Main`/topo do script do `Program.cs`, checar `args.Contains("--migrate-only")` antes de montar o host completo; quando presente, `return` o resultado de `RunMigrateOnlyAsync(args)` como código de saída do processo, sem abrir o servidor HTTP.
- [x] 1.5 Depois de `app.Build()` e antes de `app.Run()`, se `Database:MigrateOnStartup` for `true`, aplicar `AppDbContext.Database.MigrateAsync()` num scope antes do servidor aceitar tráfego.
- [x] 1.6 Teste de integração (spec `deployment`, requirement "Modo dedicado de migration", cenário "Migration bem-sucedida em modo dedicado"): com Postgres real (Testcontainers) e schema não migrado, `Program.RunMigrateOnlyAsync(["--migrate-only"])` devolve `0` e as tabelas esperadas existem depois.
- [x] 1.7 Teste de integração (cenário "Falha de migration em modo dedicado"): com o container do Postgres parado, `Program.RunMigrateOnlyAsync(["--migrate-only"])` devolve um valor diferente de `0`.
- [x] 1.8 Teste de integração (spec `deployment`, requirement "Migration no startup controlada por configuração", cenário "Sem o argumento e com a flag desabilitada, a aplicação sobe sem migrar"): com `Database:MigrateOnStartup=false` e um schema não migrado, subir a aplicação via `WebApplicationFactory<Program>` e confirmar que as tabelas de negócio ainda não existem (por exemplo, uma query direta contra `information_schema.tables` não encontra `loans`).
- [x] 1.9 Teste de integração (spec `deployment`, requirement "Migration no startup controlada por configuração", cenário "Sem o argumento e com a flag habilitada, a aplicação migra antes de aceitar tráfego"): com `Database:MigrateOnStartup=true` e um schema não migrado, subir a aplicação via `WebApplicationFactory<Program>` e confirmar que a tabela `loans` já existe assim que o host termina de subir (achado da revisão pré-archive: cenário da spec estava sem tarefa e sem teste).

## 2. Imagem de contêiner

- [x] 2.1 Criar `src/Library.Api/Dockerfile` multi-stage: estágio `build` (SDK, `dotnet restore` em camada separada da cópia do código-fonte), estágio `publish` (`dotnet publish` sem SDK extra), estágio final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` copiando só a saída de publish, com `USER $APP_UID` e `ENTRYPOINT` para `Library.Api.dll`.
- [x] 2.2 Verificar `docker build -f src/Library.Api/Dockerfile .` conclui com sucesso e a imagem resultante não contém o SDK (`docker run --rm <imagem> ls /usr/share/dotnet/sdk` falha, ou inspeção equivalente).
- [x] 2.3 Verificar `docker run --rm <imagem> --migrate-only` com um Postgres acessível aplica as migrations e encerra com código 0 (`echo $?`).

## 3. Ajustes no `docker-compose.yml`

- [x] 3.1 Remover `ASPNETCORE_ENVIRONMENT: "Development"` do serviço `api`; confirmar que nem `api` nem `migrator` definem `Database__MigrateOnStartup` (o padrão `false` de `appsettings.json` prevalece dentro dos contêineres).
- [x] 3.2 Confirmar que `api` mantém `depends_on: migrator: condition: service_completed_successfully` e `postgres`/`redis` saudáveis; ajustar se necessário para que a ordem declarada em `design.md` seja respeitada.
- [x] 3.3 Verificar manualmente (cenário 4 do enunciado, verificação manual - não em `dotnet test`): `docker compose --profile app up --build` sobe `postgres`, `redis`, `otel`, `migrator` (conclui e sai) e `api`, e `curl http://localhost:8080/health/ready` responde 200; registrar o comando e o resultado esperado no `README.md` (tarefa 5.2).
- [x] 3.4 Verificar manualmente (cenário 5 do enunciado, verificação manual): com o serviço `postgres` do compose desligado antes de subir o profile `app` (ou uma connection string inválida temporária no `migrator`), `docker compose --profile app up --build` faz o `migrator` falhar e o serviço `api` não inicia (`docker compose ps` mostra `api` ausente/não criado); registrar o comando e o resultado esperado no `README.md` (tarefa 5.2).

## 4. Manifests Kubernetes

- [x] 4.1 Criar `k8s/deployment.yaml`: `Deployment` com `replicas`, `requests`/`limits` de CPU e memória, `livenessProbe` (`httpGet` em `/health/live`) e `readinessProbe` (`httpGet` em `/health/ready`), `terminationGracePeriodSeconds` igual ao tempo de desligamento gracioso padrão da aplicação (30s), variáveis de ambiente via `envFrom` (ConfigMap e Secret).
- [x] 4.2 Criar `k8s/service.yaml`: `Service` do tipo `ClusterIP` apontando para os pods do `Deployment`.
- [x] 4.3 Criar `k8s/configmap.yaml` com a configuração não sensível (por exemplo, `Cache__*`, `Database__MigrateOnStartup=false`, endpoint OTLP).
- [x] 4.4 Criar `k8s/secret.example.yaml` com placeholders para `ConnectionStrings__Postgres` e `ConnectionStrings__Redis`; confirmar que nenhum valor real está no arquivo versionado.
- [x] 4.5 Criar `k8s/migrator-job.yaml`: `Job` rodando a mesma imagem com `command: ["--migrate-only"]`, usando o mesmo `ConfigMap`/`Secret`.
- [x] 4.6 Verificar `kubectl apply --dry-run=client -f k8s/` (ou `kubectl kustomize`/`kubeconform`, se disponível) não reporta erro de sintaxe em nenhum manifest. **Nota:** nem `kubectl` nem `kubeconform` estão disponíveis neste ambiente; a verificação real foi feita com `yaml.safe_load_all` sobre cada arquivo (confirma YAML bem-formado e um `kind` por documento), o que não substitui validação contra o schema da API do Kubernetes. Recomenda-se rodar `kubectl apply --dry-run=client -f k8s/` contra um cluster real antes do primeiro deploy.
- [x] 4.7 Teste de unidade (spec `deployment`, requirement "Manifests Kubernetes para múltiplas réplicas", cenário "Sonda de liveness e de readiness apontam para endpoints distintos"): ler `k8s/deployment.yaml` como texto e verificar que `livenessProbe` referencia `/health/live` e `readinessProbe` referencia `/health/ready` (`tests/Library.UnitTests/Infrastructure/KubernetesManifestsTests.cs`).
- [x] 4.8 Teste de unidade (cenário "Nenhum valor sensível real está versionado"): verificar que `k8s/secret.example.yaml` só contém placeholders reconhecíveis (por exemplo, `CHANGE_ME`/`<...>`) e que nenhum outro arquivo `k8s/*.yaml` declara um `kind: Secret` com dado real.

## 5. Documentação

- [x] 5.1 Criar `README.md` na raiz do repositório: como subir localmente (`dotnet run` com Postgres/Redis via `docker-compose.yml` sem o profile `app`), como subir via `docker compose --profile app up --build`, como aplicar os manifests Kubernetes na ordem correta (`migrator` Job → aguardar conclusão → `Deployment`/`Service`/`ConfigMap`/`Secret`).
- [x] 5.2 No `README.md`, documentar os dois cenários de verificação manual (spec `deployment`, requirement "Subida local completa depende da migration"): o comando exato de cada um (subida completa bem-sucedida; migration falhando) e o resultado esperado, para o avaliador repetir.
- [x] 5.3 No `README.md`, documentar como verificar manualmente os dois cenários que não são testáveis em `dotnet test` nem por leitura de arquivo: "Imagem final não contém SDK" (requirement "Imagem de contêiner mínima e não privilegiada" - comando de inspeção da tarefa 2.2) e "Total de conexões no número máximo de réplicas fica abaixo do limite do banco" (requirement "Pool de conexões dimensionado para múltiplas réplicas" - a conta 8 × 11 = 88 < 100, já registrada em `design.md`, reproduzível escalando o `Deployment` para 11 réplicas e observando `pg_stat_activity`).

## 6. Regressão

- [x] 6.1 Rodar a suíte completa (`dotnet test`) e confirmar que nenhum teste existente regride com `TreatWarningsAsErrors` ligado.

## 7. Ajustes da revisão pré-archive

- [x] 7.1 Propagar `app.Lifetime.ApplicationStopping` para as duas chamadas de `Database.MigrateAsync()` em `Program.cs` (migration no startup e `RunMigrateOnlyAsync`), em vez de `CancellationToken.None` implícito.
- [x] 7.2 Adicionar `builder.Logging.AddJsonConsole()` também ao host mínimo de `RunMigrateOnlyAsync`, para a saída do modo `--migrate-only` (incluindo os logs internos de progresso do EF Core) sair em JSON como o resto da aplicação.
