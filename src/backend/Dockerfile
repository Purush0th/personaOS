# PersonaOS API — build context is the repo root:
#   docker build -f src/backend/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first so `restore` is cached until dependencies change.
COPY src/backend/PersonaOS.Api/PersonaOS.Api.csproj            src/backend/PersonaOS.Api/
COPY src/backend/PersonaOS.Application/PersonaOS.Application.csproj src/backend/PersonaOS.Application/
COPY src/backend/PersonaOS.Domain/PersonaOS.Domain.csproj      src/backend/PersonaOS.Domain/
COPY src/backend/PersonaOS.Infrastructure/PersonaOS.Infrastructure.csproj src/backend/PersonaOS.Infrastructure/
RUN dotnet restore src/backend/PersonaOS.Api/PersonaOS.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/PersonaOS.Api/PersonaOS.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the image's non-root user; the data volume is chowned to it.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Documents__StoragePath=/app/data/docs-storage \
    DataProtection__KeysPath=/app/data/dp-keys

COPY --from=build /app/publish .
RUN mkdir -p /app/data/docs-storage /app/data/dp-keys && chown -R app:app /app/data
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "PersonaOS.Api.dll"]
