# Running WebJEA in Docker

WebJEA ships a Docker configuration for Linux containers under [docker/](../docker/). The PowerShell 7 engine is embedded in the app — but remember that scripts run **inside the container**: only the modules and network access available there are usable.

- **Dockerfile:** `docker/Dockerfile`
- **Compose file:** `docker/docker-compose.yml`
- **Base image:** `mcr.microsoft.com/dotnet/aspnet:10.0`
- **Images:** `webjea` (minimal) and `webjea:latest-pwsh` (adds interactive PowerShell for troubleshooting; see [Troubleshooting](#troubleshooting))
- **Authentication:** Entra ID only
- **Scripts volume:** `/webjea/scripts`
- **Logs volume:** `/webjea/logs`

## Entra ID app registration setup

The container uses Entra ID, which requires an app registration in your Entra ID tenant. Follow the step-by-step walkthrough in [Entra App Registration](entra-appreg.md) (create the registration, enable ID tokens, add the groups claim, create a client secret and grant Graph permission), then map the values you collected to environment variables.

In your `docker-compose.yml` or `.env` file, set:

```bash
AzureAd__Instance=https://login.microsoftonline.com/
AzureAd__TenantId=<Directory (tenant) ID from step 1>
AzureAd__ClientId=<Application (client) ID from step 1>
AzureAd__ClientSecret=<Value from step 4>
Authentication__Mode=Entra
```

If using a compose `.env` file, store the secret securely (not in version control) and reference it: `AzureAd__ClientSecret=${WEBJEA_CLIENT_SECRET}`.

## Quick start

1. Create the content folders next to the compose file:

   ```bash
   cd docker
   mkdir -p scripts logs
   # copy your webjea.json and .ps1 scripts into ./scripts
   ```

   In `scripts/webjea.json`, set `"BasePath": "/webjea/scripts"` and use Entra group object IDs, app role values, or UPNs in `PermittedGroups` (see [entra.md](entra.md#authorizing-users)).

2. Fill in the `AzureAd__*` values in `docker-compose.yml` (Entra ID is required; for Windows/AD authentication see [Installation.md](Installation.md)). Prefer a compose `.env` file or secrets over committing the client secret.

3. Build and run:

   ```bash
   docker compose up -d --build
   ```

   The app listens on port 8080 in the container (mapped to 8080 on the host).

## Multiple instances (sub-sites) and scaling

Containers replace the old IIS sub-application installs: instead of installing several credentialed instances into subfolders of one site, run **one container per instance** behind a reverse proxy. See [docker/examples/docker-compose.single.yml](../docker/examples/docker-compose.single.yml) for a single instance, and [docker/examples/docker-compose.subsites.yml](../docker/examples/docker-compose.subsites.yml) (with [docker/examples/nginx.conf](../docker/examples/nginx.conf)) for several instances behind one path-routed proxy.

### Path routing vs. host routing

- **Path routing** (`https://host/hr/` routed to an instance with `WebJEA__PathBase=/hr`): one host name and certificate serves every instance, but the proxy must **not** strip the path prefix — the app itself serves under `/hr/...`, so `proxy_pass` needs the full URI, not a trailing-slash rewrite. Each instance also needs its own Entra app registration with a matching redirect URI (`https://host/hr/signin-oidc`).
- **Host routing** (`https://hr.webjea.corp.com/`): a distinct host name per instance, proxied with a plain `proxy_pass` and no `WebJEA__PathBase` at all. Simpler than path routing if you control DNS and can issue a host name (and certificate SAN) per instance.

### Scaling replicas

WebJEA's session state is in-memory per container, so scaling an instance beyond one replica requires the proxy to keep a client pinned to the same container — the example `nginx.conf` uses `ip_hash` for this. The alternative is a distributed session store (e.g. Redis-backed session state), which WebJEA does not ship out of the box.

### Per-instance identity

Give each instance its own Entra app registration (separate client ID/secret and redirect URI), so each can carry different group/app-role assignments and Microsoft Graph permissions.

## Configuration

Anything in `appsettings.json` can be overridden with environment variables using `__` separators. The images preset:

- `WebJEA__ConfigFile` — path to `webjea.json` inside the scripts volume.
- `WEBJEA_LOG_DIR` — directory NLog writes `webjea.log` / `webjea-usage.log` to.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` — honor `X-Forwarded-Proto`/`-For` from a reverse proxy (Entra sign-in redirects require this).
- `Authentication__Mode=Entra`.

## TLS

The recommended setup is a TLS-terminating reverse proxy (nginx, Traefik, YARP, an ingress controller) in front of the container, forwarding to port 8080. The proxy handles the 80→443 redirect in that case.

If you expose the container directly and terminate TLS in Kestrel, configure the certificate purely through standard Kestrel configuration — do **not** set `WebJEA__HttpsPort`. `WebJEA:HttpsPort` is Windows-service-only: it tells `KestrelEndpoints` (see [Hosting/KestrelEndpoints.cs](../WebJEA/Hosting/KestrelEndpoints.cs)) to open a Kestrel listener itself and load a certificate by thumbprint from the Windows `LocalMachine\My` certificate store — a store that doesn't exist on Linux, and setting it without `WebJEA:CertThumbprint` also makes `KestrelEndpoints.Validate` throw at startup, crash-looping the container. Use `ASPNETCORE_URLS` plus `Kestrel__Certificates__Default__*` instead, which Kestrel honors natively:

```yaml
environment:
  ASPNETCORE_URLS: "https://+:8443;http://+:8080"
  Kestrel__Certificates__Default__Path: /https/webjea.pfx
  Kestrel__Certificates__Default__Password: "<pfx-password>"
volumes:
  - ./certs:/https:ro
```

WebJEA's own HTTP→HTTPS redirect only ever activates for `WebJEA:HttpPort`/ `HttpsPort`/`CertThumbprint` (the Windows-service hosting path) — it has no effect here, since this path deliberately leaves those unset. There is no way to get an automatic redirect out of WebJEA config in the container path; put a TLS-terminating reverse proxy in front instead (see the recommended setup above), which handles the HTTP→HTTPS redirect correctly regardless of what the container publishes internally.

## Troubleshooting

The minimal image has no interactive shell for PowerShell. To get one, build the `pwsh` variant, which is the same image plus `pwsh`:

```bash
docker build -f docker/Dockerfile --target pwsh -t webjea:latest-pwsh .
```

With compose, set `target: pwsh` and `image: webjea:latest-pwsh` in `docker-compose.yml`, then run `docker compose up -d --build`. Then open a shell as the app user:

```bash
docker exec -it <container> pwsh
```

From there you can run a registered script by hand (`& /webjea/scripts/foo.ps1`), check which modules are available (`Get-Module -ListAvailable`), or test outbound network access from inside the container.

The shell's PowerShell comes from the .NET SDK image, so its patch version can differ slightly from the engine embedded in WebJEA. Check `$PSVersionTable` if a difference matters.

Run the minimal image in production, and switch to `-pwsh` only while troubleshooting.

## Notes

- The release zip is not used for containers; the images build the app from source (`dotnet publish` in a multi-stage build).
- Telemetry is compiled with placeholder credentials in local builds, so telemetry sends are no-ops in self-built images.
- Session state and data-protection keys are in-container and ephemeral; restarting the container invalidates sessions. Mount `/home/app/.aspnet/DataProtection-Keys` if you need them to persist.
- Legacy URLs (`/webjea/*`, `/default.aspx`, `/command.aspx`) are permanently redirected by the app itself, so old links keep working in containers with no extra proxy configuration.
