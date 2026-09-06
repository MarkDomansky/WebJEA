# Running WebJEA in Docker

WebJEA ships Docker configurations for both Linux and Windows containers under
[docker/](../docker/). The PowerShell 7 engine is embedded in the app, so neither
image installs PowerShell separately — but remember that scripts run **inside the
container**: only the modules and network access available there are usable.

| | Linux | Windows |
|---|---|---|
| Dockerfile | `docker/Dockerfile` | `docker/Dockerfile.windows` |
| Compose file | `docker/docker-compose.yml` | `docker/docker-compose.windows.yml` |
| Base image | `mcr.microsoft.com/dotnet/aspnet:10.0` | `mcr.microsoft.com/dotnet/aspnet:10.0-windowsservercore-ltsc2022` |
| Authentication | Entra ID only | Windows (with a gMSA credential spec) or Entra ID |
| Scripts volume | `/webjea/scripts` | `C:\webjea/scripts` |
| Logs volume | `/webjea/logs` | `C:\webjea\logs` |

## Entra ID app registration setup

Both Linux and Windows containers using Entra ID require an app registration in your
Entra ID tenant. Follow the step-by-step walkthrough in
[entra.md — App registration setup](entra.md#app-registration-setup) (create the
registration, enable ID tokens, add the groups claim, optionally create a secret and
grant Graph permission), then map the values to environment variables.

In your `docker-compose.yml` or `.env` file, set:

```bash
AzureAd__Instance=https://login.microsoftonline.com/
AzureAd__TenantId=<Directory (tenant) ID from step 1>
AzureAd__ClientId=<Application (client) ID from step 1>
AzureAd__ClientSecret=<Value from step 3>
AzureAd__CallbackPath=/signin-oidc
Authentication__Mode=Entra
```

If using a compose `.env` file, store the secret securely (not in version control) and
reference it: `AzureAd__ClientSecret=${WEBJEA_CLIENT_SECRET}`.

## Quick start (Linux)

1. Create the content folders next to the compose file:

   ```bash
   cd docker
   mkdir -p scripts logs
   # copy your config.json and .ps1 scripts into ./scripts
   ```

   In `scripts/config.json`, set `"basepath": "/webjea/scripts"` and use
   Entra group object IDs, app role values, or UPNs in `permittedgroups` (see
   [entra.md](entra.md#authorization-modes)).

2. Fill in the `AzureAd__*` values in `docker-compose.yml` (Entra ID is required on
   Linux; Windows/AD authentication needs a Windows host). Prefer a compose
   `.env` file or secrets over committing the client secret.

3. Build and run:

   ```bash
   docker compose up -d --build
   ```

   The app listens on port 8080 in the container (mapped to 8080 on the host).

## Quick start (Windows)

Same layout with `docker-compose.windows.yml`, on a Windows host switched to
Windows-container mode. In `scripts\config.json` set
`"basepath": "C:\\webjea\\scripts"`.

The image defaults to Windows (Negotiate) authentication like the Windows service
install ([windows.md](windows.md)), but inside a container that only works with a
**gMSA credential spec**:

1. Create a gMSA and install the [CredentialSpec module](https://www.powershellgallery.com/packages/CredentialSpec)
   on the container host, then `New-CredentialSpec -AccountName <gmsa-name>`.
2. Uncomment `security_opt` in the compose file and point it at the generated spec.
3. Grant the gMSA whatever AD permissions your scripts need — the same
   least-privilege guidance as the Windows service install applies.

Without a credential spec, uncomment the `environment` block and use Entra ID.

## Multiple instances (sub-sites) and scaling

Containers replace the old IIS sub-application installs: instead of installing several
credentialed instances into subfolders of one site, run **one container per instance**
behind a reverse proxy. See
[docker/examples/docker-compose.single.yml](../docker/examples/docker-compose.single.yml)
for a single instance, and
[docker/examples/docker-compose.subsites.yml](../docker/examples/docker-compose.subsites.yml)
(with [docker/examples/nginx.conf](../docker/examples/nginx.conf)) for several instances
behind one path-routed proxy.

### Path routing vs. host routing

- **Path routing** (`https://host/hr/` routed to an instance with
  `WebJEA__PathBase=/hr`): one host name and certificate serves every instance, but the
  proxy must **not** strip the path prefix — the app itself serves under `/hr/...`, so
  `proxy_pass` needs the full URI, not a trailing-slash rewrite. Each instance also
  needs its own Entra app registration with a matching redirect URI
  (`https://host/hr/signin-oidc`).
- **Host routing** (`https://hr.webjea.corp.com/`): a distinct host name per instance,
  proxied with a plain `proxy_pass` and no `WebJEA__PathBase` at all. Simpler than path
  routing if you control DNS and can issue a host name (and certificate SAN) per
  instance.

### Scaling replicas

WebJEA's session state is in-memory per container, so scaling an instance beyond one
replica requires the proxy to keep a client pinned to the same container — the example
`nginx.conf` uses `ip_hash` for this. The alternative is a distributed session store
(e.g. Redis-backed session state), which WebJEA does not ship out of the box.

### Per-instance identity

- **Entra ID**: give each instance its own app registration (separate client ID/secret
  and redirect URI), so each can carry different group/app-role assignments and
  Microsoft Graph permissions.
- **Windows-identity scripts**: run Windows containers with a separate gMSA credential
  spec per container — see [Quick start (Windows)](#quick-start-windows) above for the
  `CredentialSpec` setup.

## Configuration

Anything in `appsettings.json` can be overridden with environment variables using
`__` separators. The images preset:

- `WebJEA__ConfigFile` — path to `config.json` inside the scripts volume.
- `WEBJEA_LOG_DIR` — directory NLog writes `webjea.log` / `webjea-usage.log` to.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` — honor `X-Forwarded-Proto`/`-For`
  from a reverse proxy (Entra sign-in redirects require this).
- Linux only: `Authentication__Mode=Entra`.

## TLS

The recommended setup is a TLS-terminating reverse proxy (nginx, Traefik, YARP, an
ingress controller) in front of the container, forwarding to port 8080 — the same
pattern as the Linux systemd deployment in
[deployment-migration.md](deployment-migration.md). The proxy handles the 80→443
redirect in that case.

If you expose the container directly and terminate TLS in Kestrel, configure the
certificate purely through standard Kestrel configuration — do **not** set
`WebJEA__HttpsPort`. `WebJEA:HttpsPort` is Windows-service-only: it tells
`KestrelEndpoints` (see [Hosting/KestrelEndpoints.cs](../WebJEA/Hosting/KestrelEndpoints.cs))
to open a Kestrel listener itself and load a certificate by thumbprint from the
Windows `LocalMachine\My` certificate store — a store that doesn't exist on Linux, and
setting it without `WebJEA:CertThumbprint` also makes `KestrelEndpoints.Validate` throw
at startup, crash-looping the container. Use `ASPNETCORE_URLS` plus
`Kestrel__Certificates__Default__*` instead, which Kestrel honors natively on both
Windows and Linux containers:

```yaml
environment:
  ASPNETCORE_URLS: "https://+:8443;http://+:8080"
  Kestrel__Certificates__Default__Path: /https/webjea.pfx
  Kestrel__Certificates__Default__Password: "<pfx-password>"
volumes:
  - ./certs:/https:ro
```

WebJEA's own HTTP→HTTPS redirect only ever activates for `WebJEA:HttpPort`/
`HttpsPort`/`CertThumbprint` (the Windows-service hosting path) — it has no effect
here, since this path deliberately leaves those unset. There is no way to get an
automatic redirect out of WebJEA config in the container path; put a
TLS-terminating reverse proxy in front instead (see the recommended setup above),
which handles the HTTP→HTTPS redirect correctly regardless of what the container
publishes internally.

## Notes

- The release zip is not used for containers; the images build the app from source
  (`dotnet publish` in a multi-stage build).
- Telemetry is compiled with placeholder credentials in local builds, so telemetry
  sends are no-ops in self-built images.
- Session state and data-protection keys are in-container and ephemeral; restarting
  the container invalidates sessions. Mount `/home/app/.aspnet/DataProtection-Keys`
  (Linux) if you need them to persist.
- Legacy URLs (`/webjea/*`, `/default.aspx`, `/command.aspx`) are permanently
  redirected by the app itself, so old links keep working in containers with no
  extra proxy configuration.
