# syntax=docker/dockerfile:1.7
# Multi-stage build for the Personal Platform API (which also serves the Blazor WebAssembly client)
# and the EF Core migration bundle. Base images are pinned by digest; update them deliberately.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4
ARG RUNTIME_DEPS_IMAGE=mcr.microsoft.com/dotnet/runtime-deps:10.0@sha256:12dd273c196e92aa91542749b8df0996f497f812811abba9e6613b484f681dd0

FROM ${SDK_IMAGE} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Optional: trust an extra CA (corporate TLS-inspecting proxy) during the build only. Passed as a BuildKit
# secret so it never ends up in an image layer:  --secret id=extra_ca,src=/path/to/ca.crt
RUN --mount=type=secret,id=extra_ca,required=false \
    if [ -f /run/secrets/extra_ca ]; then cp /run/secrets/extra_ca /usr/local/share/ca-certificates/extra_ca.crt && update-ca-certificates; fi

# Restore in a separate layer so source changes do not invalidate the package cache.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig dotnet-tools.json ./
COPY src/PersonalPlatform.Domain/PersonalPlatform.Domain.csproj src/PersonalPlatform.Domain/
COPY src/PersonalPlatform.Contracts/PersonalPlatform.Contracts.csproj src/PersonalPlatform.Contracts/
COPY src/PersonalPlatform.Application/PersonalPlatform.Application.csproj src/PersonalPlatform.Application/
COPY src/PersonalPlatform.Infrastructure/PersonalPlatform.Infrastructure.csproj src/PersonalPlatform.Infrastructure/
COPY src/PersonalPlatform.Client/PersonalPlatform.Client.csproj src/PersonalPlatform.Client/
COPY src/PersonalPlatform.Api/PersonalPlatform.Api.csproj src/PersonalPlatform.Api/
RUN --mount=type=secret,id=extra_ca,required=false \
    if [ -f /run/secrets/extra_ca ]; then cp /run/secrets/extra_ca /usr/local/share/ca-certificates/extra_ca.crt && update-ca-certificates; fi; \
    dotnet restore src/PersonalPlatform.Api/PersonalPlatform.Api.csproj \
    && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/PersonalPlatform.Api/PersonalPlatform.Api.csproj -c Release -o /out/app --no-restore \
    && dotnet ef migrations bundle \
        --project src/PersonalPlatform.Infrastructure/PersonalPlatform.Infrastructure.csproj \
        --startup-project src/PersonalPlatform.Infrastructure/PersonalPlatform.Infrastructure.csproj \
        --configuration Release --self-contained --target-runtime linux-x64 \
        --output /out/migrator/efbundle

# --- Migration job: runs once per deployment with the schema-owner credentials, never at app startup.
FROM ${RUNTIME_DEPS_IMAGE} AS migrator
WORKDIR /migrator
COPY --from=build --chown=root:root --chmod=0555 /out/migrator/efbundle ./efbundle
USER 1654:1654
ENTRYPOINT ["./efbundle"]

# --- Application.
FROM ${ASPNET_IMAGE} AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_gcServer=0
# Application files are owned by root and read-only for the runtime user ("app", uid/gid 1654 in the .NET images).
COPY --from=build --chown=root:root /out/app ./
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "PersonalPlatform.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "PersonalPlatform.Api.dll"]
