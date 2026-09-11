## Purpose

Define como a aplicação é empacotada em imagem de contêiner e implantada em múltiplas réplicas: quem aplica as migrations, quando, e o que garante que uma réplica só recebe tráfego depois que o schema está pronto.

## ADDED Requirements

### Requirement: Modo dedicado de migration
A aplicação, executada com o argumento `--migrate-only`, SHALL aplicar as migrations pendentes do banco de dados e encerrar sem abrir o servidor HTTP. Encerrar com código de saída 0 SHALL indicar que as migrations foram aplicadas com sucesso (ou já estavam aplicadas). Uma falha ao aplicar as migrations (por exemplo, banco inacessível) SHALL encerrar o processo com código de saída diferente de zero, sem abrir o servidor HTTP.

#### Scenario: Migration bem-sucedida em modo dedicado
- **WHEN** a aplicação é executada com `--migrate-only` e o banco de dados está acessível
- **THEN** as migrations pendentes são aplicadas
- **AND** o processo encerra com código de saída 0
- **AND** nenhum servidor HTTP é aberto

#### Scenario: Falha de migration em modo dedicado
- **WHEN** a aplicação é executada com `--migrate-only` e o banco de dados está inacessível
- **THEN** o processo encerra com código de saída diferente de zero
- **AND** nenhum servidor HTTP é aberto

### Requirement: Migration no startup controlada por configuração
Sem o argumento `--migrate-only`, a aplicação SHALL abrir o servidor HTTP normalmente. A aplicação SHALL aplicar migrations no início do processo de startup, antes de aceitar tráfego, somente quando uma flag de configuração dedicada estiver habilitada. Com a flag desabilitada (o padrão fora de desenvolvimento local), a aplicação SHALL subir sem aplicar nenhuma migration.

#### Scenario: Sem o argumento e com a flag desabilitada, a aplicação sobe sem migrar
- **WHEN** a aplicação é executada sem `--migrate-only` e a flag de migration no startup está desabilitada
- **THEN** o servidor HTTP abre normalmente
- **AND** nenhuma migration é aplicada durante o startup

#### Scenario: Sem o argumento e com a flag habilitada, a aplicação migra antes de aceitar tráfego
- **WHEN** a aplicação é executada sem `--migrate-only`, a flag de migration no startup está habilitada, e o banco de dados está acessível
- **THEN** as migrations pendentes são aplicadas antes de o servidor HTTP começar a aceitar tráfego

### Requirement: Imagem de contêiner mínima e não privilegiada
A imagem de contêiner final da aplicação SHALL conter apenas os artefatos publicados necessários para executá-la, sem o SDK do .NET nem código-fonte. O processo da aplicação dentro do contêiner SHALL rodar como um usuário sem privilégios administrativos. A porta HTTP em que o servidor escuta SHALL ser definida por configuração, não fixa no código.

#### Scenario: Imagem final não contém SDK
- **WHEN** a imagem de contêiner final é inspecionada
- **THEN** ela não contém as ferramentas de build (SDK) nem o código-fonte do projeto

### Requirement: Subida local completa depende da migration
Um único comando de orquestração local SHALL subir as dependências (banco de dados, cache, coletor de telemetria), aplicar as migrations pendentes, e então subir a API. A API SHALL só começar a aceitar tráfego depois que a etapa de migration tiver concluído com sucesso. Se a etapa de migration falhar, a API SHALL não subir.

#### Scenario: Subida completa com sucesso
- **WHEN** o comando de orquestração local sobe o ambiente completo e o banco de dados está saudável
- **THEN** a migration é aplicada com sucesso
- **AND** a API sobe em seguida e responde a requisições

#### Scenario: Migration falha e a API não sobe
- **WHEN** o comando de orquestração local sobe o ambiente completo e a etapa de migration falha
- **THEN** a API não é iniciada

### Requirement: Manifests Kubernetes para múltiplas réplicas
Os manifests Kubernetes da aplicação SHALL declarar: um número de réplicas configurável com limites e requisições de CPU e memória; uma sonda de liveness apontando para o endpoint de liveness da aplicação e uma sonda de readiness apontando para o endpoint de readiness; um serviço interno para expor as réplicas; configuração não sensível separada de dados sensíveis, com os dados sensíveis referenciados por indireção (nunca versionados com valores reais); e uma tarefa dedicada, executada uma única vez, responsável por aplicar as migrations antes de as réplicas da aplicação começarem a receber tráfego, com a ordem de aplicação documentada.

#### Scenario: Sonda de liveness e de readiness apontam para endpoints distintos
- **WHEN** os manifests do `Deployment` são inspecionados
- **THEN** a sonda de liveness aponta para o endpoint de liveness da aplicação
- **AND** a sonda de readiness aponta para o endpoint de readiness da aplicação, distinto do de liveness

#### Scenario: Nenhum valor sensível real está versionado
- **WHEN** os manifests relacionados a dados sensíveis são inspecionados
- **THEN** apenas um arquivo de exemplo com placeholders está presente
- **AND** nenhum valor sensível real aparece em nenhum manifest versionado

### Requirement: Pool de conexões dimensionado para múltiplas réplicas
O tamanho do pool de conexões com o banco de dados por réplica SHALL ser dimensionado de modo que o número máximo de réplicas suportado não exceda a capacidade de conexões do banco de dados, deixando margem para a tarefa de migration e acesso administrativo.

#### Scenario: Total de conexões no número máximo de réplicas fica abaixo do limite do banco
- **WHEN** o número máximo de réplicas suportado está todo em execução, cada uma com seu pool de conexões no máximo
- **THEN** o total de conexões simultâneas com o banco de dados fica abaixo do limite de conexões configurado no banco de dados
