FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/dominio/Jornada.Dominio/Jornada.Dominio.csproj src/dominio/Jornada.Dominio/
COPY src/aplicacao/Jornada.Aplicacao/Jornada.Aplicacao.csproj src/aplicacao/Jornada.Aplicacao/
COPY src/adaptadores/Jornada.Adaptadores.Memoria/Jornada.Adaptadores.Memoria.csproj src/adaptadores/Jornada.Adaptadores.Memoria/
COPY src/adaptadores/Jornada.Persistencia.Postgres/Jornada.Persistencia.Postgres.csproj src/adaptadores/Jornada.Persistencia.Postgres/
COPY src/hosts/Jornada.Api/Jornada.Api.csproj src/hosts/Jornada.Api/

RUN dotnet restore src/hosts/Jornada.Api/Jornada.Api.csproj --ignore-failed-sources
COPY src src
RUN dotnet publish src/hosts/Jornada.Api/Jornada.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# curl para o HEALTHCHECK do compose (local/compose.yaml chama /saude/pronto).
# A imagem aspnet é Debian slim — não vem com curl nem wget.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Jornada.Api.dll"]
