## Why

A aplicação já roda em 2 a 11 réplicas por desenho (concorrência, cache, observabilidade), mas ainda não existe forma de empacotá-la e implantá-la dessa forma: não há Dockerfile, o `docker-compose.yml` já declara os serviços `migrator`/`api` sob o profile `app` mas referencia um argumento `--migrate-only` que não existe no `Program.cs`, a aplicação não aplica migration nenhuma hoje (nem no startup, nem via um modo dedicado), e não há manifests Kubernetes. Sem isso, o desenho de múltiplas réplicas e migration centralizada num único `migrator` fica só no papel.

## What Changes

- Adiciona o modo `--migrate-only` ao `Program.cs`: aplica as migrations pendentes e encerra com código 0 (ou diferente de 0 se a migration falhar), sem abrir o servidor HTTP.
- Adiciona a flag de configuração `Database:MigrateOnStartup` (bool): quando `true`, a aplicação aplica migrations no startup antes de aceitar tráfego; substitui a checagem por nome de ambiente que o CLAUDE.md já registra como decisão fixada (a aplicação hoje não faz nenhuma das duas coisas).
- Dimensiona o pool de conexões do Npgsql via configuração (`Maximum Pool Size=8`), para que 11 réplicas não excedam o limite do PostgreSQL.
- Adiciona `Dockerfile` multi-stage em `src/Library.Api/`: build com SDK, publish, imagem final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` rodando como `$APP_UID`, sem SDK nem código-fonte.
- Ajusta `docker-compose.yml`: remove `ASPNETCORE_ENVIRONMENT: Development` do serviço `api` (contradiz `Database:MigrateOnStartup=false` esperado em Docker, já que carregaria `appsettings.Development.json`), remove o `healthcheck` baseado em `CMD-SHELL` que a imagem chiseled não suporta, e garante que a API só sobe depois do `migrator` concluir com sucesso (`depends_on: condition: service_completed_successfully`, já presente, a confirmar/ajustar).
- Adiciona manifests Kubernetes em `k8s/`: `Deployment`, `Service` (ClusterIP), `ConfigMap`, `Secret` de exemplo (`secret.example.yaml`), e `Job` do `migrator`, com probes apontando para `/health/live` e `/health/ready`.
- Documenta em um novo `README.md` os dois cenários de verificação manual (subida completa via `docker compose --profile app up --build` e migration falhando impedindo a API de subir) com o comando exato e o resultado esperado.

## Capabilities

### New Capabilities
- `deployment`: empacotamento em imagem de contêiner (Dockerfile multi-stage, modo `--migrate-only`, flag `Database:MigrateOnStartup`, dimensionamento do pool de conexões) e manifests Kubernetes para rodar a aplicação em múltiplas réplicas.

### Modified Capabilities

Nenhuma. Os endpoints de health (`/health/live`, `/health/ready`) e sua semântica já estão fixados em `observability`; esta change só os referencia como alvo de probe, sem alterar seu contrato.

## Impact

- Código novo: modo `--migrate-only` e leitura de `Database:MigrateOnStartup` em `Program.cs` (ou em `Extensions/DatabaseExtensions.cs`, a decidir em `design.md`), `src/Library.Api/Dockerfile`, manifests em `k8s/` (`deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.example.yaml`, `migrator-job.yaml`), `README.md`.
- Código existente tocado: `appsettings.json`/`appsettings.Development.json` (nova seção `Database`, `MigrateOnStartup: true` só no `Development`), `Extensions/DatabaseExtensions.cs` (pool de conexões), `docker-compose.yml` (variáveis de ambiente do serviço `api`, remoção do `healthcheck` incompatível com a imagem chiseled).
- Dependências novas: nenhuma — o modo `--migrate-only` usa `AppDbContext.Database.MigrateAsync()`, já disponível via `Microsoft.EntityFrameworkCore.Design`/`Npgsql.EntityFrameworkCore.PostgreSQL`, ambos já referenciados.
- Testes: `tests/Library.IntegrationTests` ganha testes cobrindo os cenários 1-3 do enunciado (`--migrate-only` aplica e sai com 0; `--migrate-only` com banco inacessível sai com código diferente de 0; sem o argumento e com a flag `false`, a aplicação sobe sem aplicar migrations). Os cenários 4 e 5 (subida completa via Compose e migration falhando impedindo a API de subir) são verificação manual, documentada no `README.md`.
