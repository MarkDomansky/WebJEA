# WebJEA on Linux.
# Build from the repository root:
#   docker build -f docker/Dockerfile -t webjea .
#
# Linux hosting requires Entra ID authentication (Authentication__Mode=Entra);
# Windows/AD authentication needs a Windows host. Scripts execute under the
# PowerShell 7 engine that ships inside the app, so no separate pwsh install is
# needed, but Windows-specific modules are unavailable.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore as its own layer so dependency downloads are cached between builds.
COPY WebJEA/WebJEA.csproj WebJEA/
RUN dotnet restore WebJEA/WebJEA.csproj

COPY WebJEA/ WebJEA/
RUN dotnet publish WebJEA/WebJEA.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# /webjea/scripts holds config.json and the PowerShell scripts; /webjea/logs
# receives the NLog output (WEBJEA_LOG_DIR is read by the shipped NLog.config).
RUN mkdir -p /webjea/scripts /webjea/logs && chown -R $APP_UID /webjea
VOLUME ["/webjea/scripts", "/webjea/logs"]

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    WEBJEA_LOG_DIR=/webjea/logs \
    WebJEA__ConfigFile=/webjea/scripts/config.json \
    Authentication__Mode=Entra

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "WebJEA.dll"]
