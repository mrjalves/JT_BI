# ==============================================================================
# Dockerfile para JT_BI (.NET 10 Blazor Server)
# ==============================================================================

# Estágio 1: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Estágio 2: SDK e Compilação
# Nota: Como JT_BI possui ProjectReferences para ../JT_ApplicationLayer,
# o build docker é executado a partir do diretório raiz que contém ambas as pastas,
# ou empacotando os binários publicados.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar projetos necessários para restore em cache
COPY ["JT_BI/JT_BI.csproj", "JT_BI/"]
COPY ["JT_ApplicationLayer/JT_Classes/JT_Classes.csproj", "JT_ApplicationLayer/JT_Classes/"]
COPY ["JT_ApplicationLayer/JT_Servicos/JT_Servicos.csproj", "JT_ApplicationLayer/JT_Servicos/"]
COPY ["JT_ApplicationLayer/JT_ApplicationLayer/JT_Aplicacao.csproj", "JT_ApplicationLayer/JT_ApplicationLayer/"]
COPY ["JT_ApplicationLayer/JT_UserInterface.Shared/JT_UserInterface.Shared.csproj", "JT_ApplicationLayer/JT_UserInterface.Shared/"]

RUN dotnet restore "JT_BI/JT_BI.csproj"

# Copiar todo o código-fonte restante
COPY JT_BI/ JT_BI/
COPY JT_ApplicationLayer/ JT_ApplicationLayer/

WORKDIR "/src/JT_BI"
RUN dotnet build "JT_BI.csproj" -c Release -o /app/build

# Estágio 3: Publish
FROM build AS publish
RUN dotnet publish "JT_BI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Estágio 4: Final Imagem de Produção
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Preparar diretórios de persistência com permissões adequadas
USER root
RUN mkdir -p /app/Data /app/logs && chown -R app:app /app/Data /app/logs

# Usuário não-root para segurança
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "JT_BI.dll"]
