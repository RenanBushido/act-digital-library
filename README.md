# Library API

Catálogo e empréstimos de uma biblioteca. Desafio técnico em .NET 10, projetado para rodar em 2 a 11 réplicas atrás de um orquestrador.

## Rodando localmente com `dotnet run`

Suba só as dependências (sem o profile `app`, que exige build de imagem):

```bash
docker compose up -d postgres redis otel
dotnet run --project src/Library.Api
```

Em `Development` (`appsettings.Development.json`), `Database:MigrateOnStartup` é `true`: a aplicação aplica as migrations pendentes sozinha ao subir.

## Subindo tudo via Docker Compose (`--profile app`)

Um único comando sobe banco, cache, coletor OTLP, aplica as migrations (`migrator`) e só então sobe a API:

```bash
docker compose --profile app up --build
```

**Resultado esperado:** os serviços `postgres`, `redis` e `otel` sobem; `migrator` roda `--migrate-only`, aplica as migrations pendentes e encerra com código 0; só depois disso o serviço `api` inicia. Confirme com:

```bash
curl http://localhost:8080/health/ready
# {"status":"Healthy","checks":{"postgres":{"status":"Healthy",...},"redis":{"status":"Healthy",...}}}
# HTTP 200
```

A aplicação em Docker nunca aplica migration no startup (`Database:MigrateOnStartup=false`, o padrão fora de `Development`) - quem migra é sempre o `migrator`.

### Cenário: migration falhando impede a API de subir

Para reproduzir (o `migrator` precisa falhar de fato; por exemplo, com uma credencial errada temporária no serviço `migrator` do `docker-compose.yml`, ou com o `postgres` inacessível):

```bash
docker compose --profile app up --build
```

**Resultado esperado:** o serviço `migrator` encerra com código diferente de 0; o Compose reporta `service "migrator" didn't complete successfully: exit 1` (ou equivalente) e **não** cria/inicia o serviço `api` - `docker compose ps` mostra `api` no estado `Created`, nunca `Up`, e a porta 8080 não responde.

## Verificações manuais adicionais

Estes dois cenários não são cobertos por `dotnet test` nem por leitura de arquivo - dependem de inspecionar a imagem/execução real:

**A imagem final não contém o SDK do .NET:**

```bash
docker build -f src/Library.Api/Dockerfile -t library-api:local .
docker run --rm --entrypoint dotnet library-api:local --list-sdks
```

**Resultado esperado:** `No SDKs were found.`

**O pool de conexões (`Maximum Pool Size=8`) não excede o limite do PostgreSQL com o número máximo de réplicas (11):**

A conta está fixada em `Extensions/DatabaseExtensions.cs` (`MaxPoolSize = 8`, aplicado a toda `ConnectionStrings:Postgres`): `8 × 11 = 88`, abaixo do `max_connections` padrão do PostgreSQL (100), com folga para o `migrator` e acesso administrativo. Para reproduzir num cluster real, escale o `Deployment` para 11 réplicas e observe `SELECT count(*) FROM pg_stat_activity;` no Postgres permanecer abaixo de 100 mesmo sob carga.

## Kubernetes

Manifests em `k8s/` (YAML puro, sem Helm). Antes de aplicar:

1. Copie `k8s/secret.example.yaml` para `k8s/secret.yaml` (ignorado pelo git) e substitua os placeholders (`CHANGE_ME`) pelos valores reais de `ConnectionStrings__Postgres`/`ConnectionStrings__Redis`. **Nunca** versione `k8s/secret.yaml` com valores reais.
2. Ajuste `image: library-api:latest` em `k8s/deployment.yaml` e `k8s/migrator-job.yaml` para apontar ao registry usado no seu cluster.

Ordem de aplicação (o `Job` de migration precisa concluir antes do `Deployment` subir, para nenhuma réplica servir tráfego contra um schema desatualizado):

```bash
kubectl apply -f k8s/configmap.yaml -f k8s/secret.yaml
kubectl apply -f k8s/migrator-job.yaml
kubectl wait --for=condition=complete --timeout=120s job/library-api-migrator
kubectl apply -f k8s/deployment.yaml -f k8s/service.yaml
```

`k8s/migrator-job.yaml` é um `Job` (não um initContainer) porque precisa rodar uma única vez, independente de quantas réplicas o `Deployment` declarar - um initContainer rodaria uma vez por pod, disparando a migration N vezes em paralelo a cada rollout.

## Observabilidade

- `GET /health/live` - liveness, não consulta dependências.
- `GET /health/ready` - readiness, consulta PostgreSQL e Redis; Redis indisponível deixa a aplicação degradada mas pronta (200), PostgreSQL indisponível a torna não pronta (503).
- Métricas de negócio no `Meter Library.Loans` e traces exportados via OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`) - veja o dashboard Aspire em `http://localhost:18888` ao rodar via Compose.

## Escopo e limitações declaradas

Fora de escopo: Helm, pipeline de CI, autoscaling (HPA), Ingress, NetworkPolicy, provisionamento do cluster. Sem proteção contra cache stampede (réplicas podem recalcular o mesmo item de cache ao expirar simultaneamente); `HybridCache` resolveria isso ao custo de um L1 local desatualizado.
