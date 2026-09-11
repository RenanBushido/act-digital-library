# syntax=docker/dockerfile:1

# ---- build: SDK completo, restore em camada própria para aproveitar cache do Docker ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Library.Api/Library.Api.csproj src/Library.Api/
RUN dotnet restore src/Library.Api/Library.Api.csproj

COPY src/Library.Api/ src/Library.Api/
RUN dotnet publish src/Library.Api/Library.Api.csproj -c Release -o /app/publish --no-restore

# ---- final: sem SDK nem código-fonte, chiseled (sem shell, sem gerenciador de pacotes) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Library.Api.dll"]
