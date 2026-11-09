# syntax=docker/dockerfile:1.7
# Build context: repository root  →  docker build -f src/Services/Claims/Reroute.Claims.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Restore layer: copy only build metadata + project files so `dotnet restore` is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/Reroute.SharedKernel/*.csproj src/BuildingBlocks/Reroute.SharedKernel/
COPY src/BuildingBlocks/Reroute.BuildingBlocks/*.csproj src/BuildingBlocks/Reroute.BuildingBlocks/
COPY src/BuildingBlocks/Reroute.Contracts/*.csproj src/BuildingBlocks/Reroute.Contracts/
COPY src/Services/Claims/Reroute.Claims.Domain/*.csproj src/Services/Claims/Reroute.Claims.Domain/
COPY src/Services/Claims/Reroute.Claims.Application/*.csproj src/Services/Claims/Reroute.Claims.Application/
COPY src/Services/Claims/Reroute.Claims.Infrastructure/*.csproj src/Services/Claims/Reroute.Claims.Infrastructure/
COPY src/Services/Claims/Reroute.Claims.Api/*.csproj src/Services/Claims/Reroute.Claims.Api/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Services/Claims/Reroute.Claims.Api/Reroute.Claims.Api.csproj

# 2) Build layer
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Claims/ src/Services/Claims/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Services/Claims/Reroute.Claims.Api/Reroute.Claims.Api.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

# 3) Runtime: chiseled = no shell, no package manager, non-root by default (~110 MB).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Reroute.Claims.Api.dll"]
