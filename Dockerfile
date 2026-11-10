FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore em camada separada para aproveitar o cache do Docker.
COPY ["src/wrench.billing.domain/wrench.billing.domain.csproj", "src/wrench.billing.domain/"]
COPY ["src/wrench.billing.application/wrench.billing.application.csproj", "src/wrench.billing.application/"]
COPY ["src/wrench.billing.infra/wrench.billing.infra.csproj", "src/wrench.billing.infra/"]
COPY ["src/wrench.billing.api/wrench.billing.api.csproj", "src/wrench.billing.api/"]
RUN dotnet restore "src/wrench.billing.api/wrench.billing.api.csproj"

COPY src/ src/
RUN dotnet publish "src/wrench.billing.api/wrench.billing.api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "wrench.billing.api.dll"]