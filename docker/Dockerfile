# syntax=docker/dockerfile:1
# (Must stay the first line: `RUN --mount=type=secret,env=` needs Dockerfile
# syntax 1.10+, which this pins regardless of the local Docker version.)
#
# WebJEA on Linux.
#
# Prebuilt multi-arch images (linux/amd64, linux/arm64) are published by the
# release workflow - most deployers should pull rather than build:
#   ghcr.io/markdomansky/webjea:<tag>          (Docker Hub mirror: markdomansky/webjea)
#   ghcr.io/markdomansky/webjea:<tag>-pwsh     (+ pwsh)
#
# Build from the repository root:
#   docker build -f docker/Dockerfile -t webjea .                              (minimal)
#   docker build -f docker/Dockerfile --target pwsh -t webjea:latest-pwsh .    (+ pwsh)
# Add --build-arg VERSION=<semver> to stamp a version (CI passes the release's).
#
# Linux hosting requires Entra ID authentication (Authentication__Mode=Entra);
# Windows/AD authentication needs a Windows host. Scripts execute under the
# PowerShell 7 engine that ships inside the app, so the minimal image needs no
# separate pwsh install, but Windows-specific modules are unavailable. The pwsh
# target adds an interactive shell for troubleshooting only.

# The SDK always runs on the build host's own architecture and cross-compiles
# for the target (-a $TARGETARCH), so a multi-arch build never runs dotnet
# under QEMU emulation - only the trivial RUN steps in the final stages do.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# Same default as build.psake.ps1; dotnet accepts semver here and stamps it as
# the InformationalVersion (e.g. 2100.0.0-alpha.1).
ARG VERSION=0.0.0-local
WORKDIR /src

# Restore as its own layer so dependency downloads are cached between builds.
COPY WebJEA/WebJEA.csproj WebJEA/
RUN dotnet restore WebJEA/WebJEA.csproj -a $TARGETARCH

COPY WebJEA/ WebJEA/

# Telemetry credentials, mirroring build.psake.ps1's GenerateAwsSecrets: the
# release workflow passes them as BuildKit secrets (never stored in a layer or
# the build cache); when any is missing - local and PR builds - the csproj
# compiles the template's placeholders instead. Generated, compiled and
# deleted in one step so no layer keeps AwsSecrets.cs.
RUN --mount=type=secret,id=AWS_KEY,env=AWS_KEY \
    --mount=type=secret,id=AWS_KEYSEC,env=AWS_KEYSEC \
    --mount=type=secret,id=AWS_QUEUE_URL,env=AWS_QUEUE_URL \
    generated=; \
    if [ -n "$AWS_KEY" ] && [ -n "$AWS_KEYSEC" ] && [ -n "$AWS_QUEUE_URL" ]; then \
        pwsh -NoLogo -NoProfile -Command ' \
            $c = Get-Content WebJEA/Telemetry/AwsSecrets.template.cs -Raw; \
            foreach ($n in "AWS_KEY", "AWS_KEYSEC", "AWS_QUEUE_URL") { $c = $c.Replace("{{$n}}", [Environment]::GetEnvironmentVariable($n)) }; \
            Set-Content WebJEA/Telemetry/AwsSecrets.cs $c.Trim()' \
        && generated=1 && echo "Generated AwsSecrets.cs from build secrets"; \
    else \
        echo "Telemetry build secrets not provided; building with placeholder values"; \
    fi \
    && dotnet publish WebJEA/WebJEA.csproj --no-restore -a $TARGETARCH --configuration Release -p:Version=$VERSION --output /app/publish \
    && if [ -n "$generated" ]; then rm WebJEA/Telemetry/AwsSecrets.cs; fi

# Source of the troubleshooting pwsh. Deliberately the TARGET platform's SDK
# (unlike `build`): pwsh's launcher is a native executable, so it must match
# the image's architecture. Nothing runs in this stage; it is only copied from.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS pwsh-src

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# /webjea/scripts holds webjea.json and the PowerShell scripts; /webjea/logs
# receives the NLog output (WEBJEA_LOG_DIR is read by the shipped NLog.config).
RUN mkdir -p /webjea/scripts /webjea/logs && chown -R $APP_UID /webjea
VOLUME ["/webjea/scripts", "/webjea/logs"]

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    WEBJEA_LOG_DIR=/webjea/logs \
    WebJEA__ConfigFile=/webjea/scripts/webjea.json \
    Authentication__Mode=Entra

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "WebJEA.dll"]

# Troubleshooting variant: the runtime image plus an interactive pwsh
# (docker exec -it <container> pwsh). The SDK image ships pwsh as a
# framework-dependent global tool that runs on this image's .NET runtime; the
# app itself does not use it.
FROM runtime AS pwsh
USER root
COPY --from=pwsh-src /usr/share/powershell /usr/share/powershell
RUN ln -s /usr/share/powershell/pwsh /usr/bin/pwsh
USER $APP_UID

# A plain `docker build` (no --target) builds the last stage, so end on the
# minimal image.
FROM runtime
