# PersonaOS API — build context is the repo root:
#   docker build -f src/backend/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The build rules (warnings as errors, analyzer levels) and the analyzer settings they rely on:
# without .editorconfig the rules it turns off would fail the build here.
COPY .editorconfig ./
COPY src/backend/Directory.Build.props src/backend/

# Copy only project files first so `restore` is cached until dependencies change.
COPY src/backend/PersonaOS.Api/PersonaOS.Api.csproj            src/backend/PersonaOS.Api/
COPY src/backend/PersonaOS.Application/PersonaOS.Application.csproj src/backend/PersonaOS.Application/
COPY src/backend/PersonaOS.Domain/PersonaOS.Domain.csproj      src/backend/PersonaOS.Domain/
COPY src/backend/PersonaOS.Infrastructure/PersonaOS.Infrastructure.csproj src/backend/PersonaOS.Infrastructure/
RUN dotnet restore src/backend/PersonaOS.Api/PersonaOS.Api.csproj

COPY src/backend/ src/backend/
# The release this image is, from the tag (.github/workflows/release.yml). The API reports it, and
# the update check compares it with the published releases; a local build is "0.0.0-dev".
ARG VERSION=0.0.0-dev
RUN dotnet publish src/backend/PersonaOS.Api/PersonaOS.Api.csproj \
    -c Release -o /app/publish --no-restore -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the image's non-root user; the data volume is chowned to it.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Documents__StoragePath=/app/data/docs-storage \
    DataProtection__KeysPath=/app/data/dp-keys

COPY --from=build /app/publish .
# /app/updates is shared with the updater container (deploy/docker-compose.yml): the API writes
# its requests there, so a fresh volume must start out owned by the API's user.
RUN mkdir -p /app/data/docs-storage /app/data/dp-keys /app/updates && chown -R app:app /app/data /app/updates
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "PersonaOS.Api.dll"]
