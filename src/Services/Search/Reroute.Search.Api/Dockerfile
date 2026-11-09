# syntax=docker/dockerfile:1.7
# Build context: repository root  →  docker build -f src/Services/Search/Reroute.Search.Api/Dockerfile .
# (See src/Services/Claims/Reroute.Claims.Api/Dockerfile for the fully layer-cache-optimised variant.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages     dotnet publish src/Services/Search/Reroute.Search.Api/Reroute.Search.Api.csproj -c Release -o /app /p:UseAppHost=false

# Chiseled runtime: no shell, no package manager, non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_gcServer=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Reroute.Search.Api.dll"]
