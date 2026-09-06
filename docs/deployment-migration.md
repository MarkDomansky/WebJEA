# Deployment (ASP.NET Core migration)

WebJEA is an ASP.NET Core application on .NET 10. The release zip's `site/` folder is a
`dotnet publish` output rather than a WebForms site, which changes how it is hosted.
Docker hosting (Linux and Windows containers) is covered separately in
[docker.md](docker.md).

## Windows: Deploy.ps1 (Windows service)

`Deploy.ps1` (shipped in the release zip) installs and configures a self-contained
Windows Server deployment. It hosts WebJEA as a Kestrel-backed **Windows service** —
IIS is not installed or used.

1. Copy the extracted release to the server, fill in `settings.template.json`
   (service name, gMSA identity, listener ports, certificate thumbprint, scripts/log
   paths — see [Installation.md](Installation.md#settings-reference) for every
   setting and the schema changes below), and save it as e.g. `settings.json`.
2. From an elevated **PowerShell 7** (`pwsh`) prompt — not Windows PowerShell 5.1 —
   run: `.\Deploy.ps1 -SettingsFile .\settings.json`
   (add `-TestOnly` to see what would change, or restrict the run with
   `-OnlySections PowerShell,Server,Service,WebJEA,Finalize`).

What it does:

- Copies the self-contained `dotnet publish` output to `SitePath` — no **.NET Hosting
  Bundle**, IIS features, or any other runtime prerequisite to install on the server;
  `WebJEA.exe` runs standalone. A running service is stopped first so the copy isn't
  blocked by locked files.
- Creates the **`WebJEA`** Windows service (name configurable via `ServiceName`)
  running `WebJEA.exe` as the gMSA (or domain/local account), set to **delayed
  auto-start** with **restart-on-failure** (5s/10s/30s backoff, daily reset). The
  **Logon as a Service** right is granted to the account directly (previously granted
  to the IIS `APPPOOL\` principal).
- Grants the service account **Read** on the HTTPS certificate's private key file
  (when `HttpsPort` and `CertThumbprint` are set). Without this the account must be a
  local administrator to open the key, because the `LocalMachine\My` store is readable
  by everyone but the key file itself is not — see
  [System Requirements](System-Requirements.md#certificate-recommended).
- Writes `appsettings.Production.json` in the site folder: the `config.json` path,
  `WebJEA:HttpPort`/`WebJEA:HttpsPort`/`WebJEA:CertThumbprint` (HTTP is automatically
  redirected to HTTPS once all three are set — there is no separate setting for it),
  and the top-level `AllowedHosts` (every configured FQDN plus the machine name and
  `localhost` — this replaces IIS host-header bindings; requests for other host names
  get HTTP 400). Because `appsettings.Production.json` is not part of the shipped
  site files, it survives in-place upgrades. NLog's log-file paths are written the
  same way as before.
- Opens a Windows Firewall inbound rule for each enabled port.
- Verifies (and attempts to register) the Kerberos SPN for every FQDN — see
  [windows.md](windows.md#kerberos-requires-an-spn) — unless `-SkipSpnCheck` is passed.
- Starts (or restarts) the service and smoke-probes every configured http/https
  binding through `127.0.0.1`, failing the deploy if the app doesn't answer.

**WebJEA needs exclusive use of its configured ports** — nothing else on the host,
including IIS, can be bound to `HttpPort`/`HttpsPort`.

Legacy `/webjea/*` and `*.aspx` URLs are still permanently redirected by the app
itself, always on. The old `EnableBackwardCompatibility` setting and the
`site-redirect/` companion IIS app from earlier ASP.NET Core releases are gone
entirely along with IIS.

PowerShell 7 does **not** need a separate install on the server for WebJEA itself —
the engine ships inside the site folder and WebJEA hosts it in-process. `Deploy.ps1`
is a different matter: it is written for PowerShell 7 and must be **run** from
`pwsh` (PowerShell 7), elevated — see the step above. Running it from Windows
PowerShell 5.1 (`powershell.exe`) fails with parse errors before the script gets a
chance to print a friendly message.

### Settings schema changes (from the IIS-era settings file)

| Old key | New key | Notes |
|---|---|---|
| `SiteName`, `AppPoolName` | `ServiceName` | Windows service name; defaults to `WebJEA` |
| `AppPoolUserName`, `AppPoolPassword` | `ServiceUserName`, `ServicePassword` | Same gMSA-vs-password rule: gMSA ⇒ trailing `$`, empty password; otherwise a password is required |
| `AppPoolLoadUserProfile` | *(removed)* | Not applicable to a Windows service |
| `DisableDefaultWebsite` | *(removed)* | No IIS site to disable |
| *(new)* | `HttpPort`, `HttpsPort` | Port number to listen on; `0` or omitted disables that listener. At least one is required; `HttpsPort` requires `CertThumbprint` |
| `RedirectPort80To443` | *(removed)* | HTTP is redirected to HTTPS automatically whenever `HttpPort`, `HttpsPort`, and `CertThumbprint` are all set; the redirect is no longer independently settable |
| `SiteFQDN`, `SecondarySiteFQDNs` | `SiteFQDNs` | One array holding every host name WebJEA answers on; there is no primary/secondary distinction (all names were already treated identically for allowed hosts, SPNs, certificate coverage and the smoke probe). A single name may also be given as a plain string. Each entry is validated as a host name — no scheme, port, path or wildcard |
| `AppName`, `ParentSiteName` | *(removed)* | Sub-application installs are no longer supported on Windows — see below |

A settings file that still contains any retired key (`SiteName`, `AppPoolName`,
`AppPoolUserName`, `AppPoolPassword`, `AppPoolLoadUserProfile`,
`DisableDefaultWebsite`, `AppName`, `ParentSiteName`, `SiteFQDN`,
`SecondarySiteFQDNs`) is rejected outright with an error naming the offending keys —
Deploy.ps1 does not attempt a partial or best-effort translation of an old file.

### Migrating an existing IIS install

1. Stop and remove the old WebJEA IIS site and app pool(s) (`Remove-Website`,
   `Remove-WebAppPool`), or at minimum free up ports 80/443 so the new service can
   bind them.
2. Your existing `ScriptsPath`, `config.json`, and log files are reused as-is —
   nothing to change there.
3. Convert your settings file to the new schema (see the table above) and run
   `Deploy.ps1` against it.
4. Deploy.ps1 refuses to run while IIS has site bindings on the ports your
   settings request (or when the `W3SVC` service exists but its bindings can't be
   verified), so it can't half-migrate a server out from under a live IIS install.
   IIS serving unrelated sites on other ports is fine and does not block the
   deployment. Pass `-IgnoreExistingIIS` to skip the check entirely once you've
   confirmed any remaining conflict is resolved.
5. Once the new install is verified, the old site folder can be deleted.

### Sub-application installs are no longer supported

The previous sub-application model — installing additional WebJEA instances into
subfolders of a site, each with its own app pool and credential
(`settings-subapp.template.jsonc`) — was removed along with IIS hosting. Each former
sub-app now runs as its own **container instance** behind a reverse proxy; see
[docker.md — Multiple instances (sub-sites) and scaling](docker.md#multiple-instances-sub-sites-and-scaling).
If you're not ready to move to containers, stay on the previous WebJEA release until
you are.

## Linux (Debian / RHEL)

There is no automated installer for Linux; the manual steps are below. On Linux,
`Authentication:Mode` must be `Entra` (Windows/AD auth requires a Windows host) — see
[authentication.md](authentication.md). Scripts execute under PowerShell 7 on Linux, so
Windows-specific modules are unavailable ([powershell7.md](powershell7.md)).

### 1. Install the ASP.NET Core 10 runtime

**Debian 12/13:**

```bash
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0
```

**RHEL 8/9/10 (and compatible):** .NET ships in Red Hat's AppStream repositories:

```bash
sudo dnf install -y aspnetcore-runtime-10.0
```

### 2. Lay out the application

```bash
sudo useradd --system --create-home --shell /usr/sbin/nologin webjea
sudo mkdir -p /opt/webjea/site /opt/webjea/scripts /var/log/webjea
# unzip the release; its site/ folder becomes /opt/webjea/site, its scripts/ folder seeds /opt/webjea/scripts
sudo chown -R webjea:webjea /opt/webjea /var/log/webjea
```

Then adjust the configuration for Linux paths:

- `/opt/webjea/scripts/config.json`: set `"basepath": "/opt/webjea/scripts"`.
- Log location: either set the `WEBJEA_LOG_DIR` environment variable (the shipped
  `NLog.config` reads it; see the systemd unit below) or edit both `fileName`
  attributes in `/opt/webjea/site/NLog.config`.
- `/opt/webjea/site/appsettings.Production.json` (create it):

```json
{
  "WebJEA": { "ConfigFile": "/opt/webjea/scripts/config.json" },
  "Authentication": { "Mode": "Entra" },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-guid>",
    "ClientId": "<client-id>",
    "ClientSecret": "<secret>",
    "CallbackPath": "/signin-oidc"
  }
}
```

### 3. Run as a systemd service

`/etc/systemd/system/webjea.service`:

```ini
[Unit]
Description=WebJEA
After=network.target

[Service]
WorkingDirectory=/opt/webjea/site
ExecStart=/usr/bin/dotnet /opt/webjea/site/WebJEA.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=webjea
User=webjea
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
Environment=WEBJEA_LOG_DIR=/var/log/webjea

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now webjea
```

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the app honor `X-Forwarded-Proto`/
`X-Forwarded-For` from the reverse proxy, which Entra sign-in redirects require.

### 4. Reverse proxy (nginx example)

```nginx
server {
    listen 443 ssl;
    server_name webjea.example.com;
    ssl_certificate     /etc/ssl/certs/webjea.crt;
    ssl_certificate_key /etc/ssl/private/webjea.key;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        # script executions can be long-running
        proxy_read_timeout 300s;
    }
}
```

On RHEL with SELinux enforcing, allow nginx to proxy to the app:

```bash
sudo setsebool -P httpd_can_network_connect 1
```

Register `https://webjea.example.com/signin-oidc` as the redirect URI on the Entra app
registration.

### Upgrades (Linux)

Stop the service, replace `/opt/webjea/site` with the new release's `site/` folder,
restore your `appsettings.Production.json` and `NLog.config` edits (or keep them under
version control), and start the service again.

## What no longer applies (vs. the WebForms releases)

- No .NET Framework 4.8, no WebForms, no `packages/` content copying.
- No IIS URL Rewrite module: the HTTP→HTTPS redirect is performed by the app itself,
  automatically, whenever `WebJEA:HttpPort`/`HttpsPort`/`CertThumbprint` are all
  configured; legacy `/webjea/*` and `*.aspx` URLs are permanently redirected by the
  app as well.
- `Web.config` transforms (`Web.Debug/Release/Remote.config`) are replaced by
  `appsettings.{Environment}.json` + `ASPNETCORE_ENVIRONMENT`.
- The `jQueryVersion`/`jQueryUIVersion` appSettings are gone — front-end libraries ship
  under `wwwroot/lib` with fixed names.
- In-place upgrade guidance: replace the `site/` contents but keep your
  `appsettings.Production.json` and `NLog.config`. Upgrading from a WebForms
  (pre-.NET 10) release is **not** an in-place upgrade — deploy fresh with Deploy.ps1
  and point it at your existing scripts folder.
- **Breaking change:** `WebJEA:HttpsPort` now also enables the HTTPS listener; it is
  no longer a redirect-target-only setting (see the settings schema table above).
