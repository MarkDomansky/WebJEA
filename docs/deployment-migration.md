# Deployment (ASP.NET Core migration)

WebJEA is an ASP.NET Core application on .NET 10. The release zip's `site/` folder is a `dotnet publish` output rather than a WebForms site, which changes how it is hosted. Docker hosting (Linux containers) is covered separately in [docker.md](docker.md).

## Windows: Deploy.ps1 (Windows service)

`Deploy.ps1` (shipped in the release zip) installs and configures a self-contained Windows Server deployment. It hosts WebJEA as a Kestrel-backed **Windows service** — IIS is not installed or used.

1. Copy the extracted release to the server, copy `settings.template.json` to `settings.json` and fill in with server specific settings. (service name, gMSA identity, listener ports, certificate subject or PFX path, scripts/log paths — see [Installation.md](Installation.md#settings-reference) for every setting and the schema changes below), save.
2. From an elevated prompt — **Windows PowerShell 5.1 (`powershell.exe`) or PowerShell 7 (`pwsh`)**, either works — run: `.\Deploy.ps1 -SettingsFile .\settings.json` (add `-TestOnly` to see what would change, or restrict the run with `-OnlySections Server,Service,WebJEA,Finalize`). Passwords are parameters, never settings: add `-ServicePassword` for a regular (non-gMSA) service account and `-CertPassword` for a password-protected PFX.

What it does:

- Copies the self-contained `dotnet publish` output to `SitePath` — no **.NET Hosting Bundle**, IIS features, or any other runtime prerequisite to install on the server; `WebJEA.exe` runs standalone. A running service is stopped first so the copy isn't blocked by locked files.
- Grants the service account the access it needs on `SitePath` and `ScriptsPath` (read & execute) and `LogPath` (modify, creating the folder), only where it doesn't already have it. Existing permissions are never removed or changed; a folder open to every user on the server gets a warning instead — see [Folder permissions](System-Requirements.md#folder-permissions).
- Creates the **`WebJEA`** Windows service (name configurable via `ServiceName`) running `WebJEA.exe` as the gMSA (or domain/local account), set to **delayed auto-start** with **restart-on-failure** (5s/10s/30s backoff, daily reset). The **Logon as a Service** right is granted to the account directly (previously granted to the IIS `APPPOOL\` principal).
- Before changing anything, checks the certificate Kestrel will serve — the PFX at `CertPath`, or the `CertSubject` match that expires last. It stops only if Kestrel couldn't serve it: no private key, no Server Authentication, or a PFX that won't open. An untrusted, out-of-date or name-mismatched certificate is a **warning**, and the deploy carries on — see [System Requirements](System-Requirements.md#certificate-recommended).
- Grants the service account **Read** on what Kestrel reads (when `HttpsPort` is set): the PFX's folder for `CertPath`, or the private key file of every certificate matching `CertSubject` — all of them, so a renewal that is already imported works on the next restart. Without this the account must be a local administrator to open a store key, because the `LocalMachine\My` store is readable by everyone but the key file itself is not.
- Writes `appsettings.Production.json` in the site folder: the `webjea.json` path; the listeners as Kestrel's native `Kestrel:Endpoints` configuration, with the HTTPS certificate loaded from the `CertPath` file or selected from `LocalMachine\My` by subject (HTTP is automatically redirected to HTTPS whenever there is an HTTPS listener — there is no separate setting for it); and the top-level `AllowedHosts` (every configured FQDN plus the machine name and `localhost` — this replaces IIS host-header bindings; requests for other host names get HTTP 400). Because `appsettings.Production.json` is not part of the shipped site files, it survives in-place upgrades. NLog's log-file paths are written the same way as before.
- Opens a Windows Firewall inbound rule for each enabled port.
- Verifies (and attempts to register) the Kerberos SPN for every FQDN — see [windows.md](windows.md#kerberos-requires-an-spn) — unless `-SkipSpnCheck` is passed.
- Starts (or restarts) the service and smoke-probes every configured http/https binding through `127.0.0.1`, failing the deploy if the app doesn't answer.

**WebJEA needs exclusive use of its configured ports** — nothing else on the host, including IIS, can be bound to `HttpPort`/`HttpsPort`.

Legacy `/webjea/*` and `*.aspx` URLs are still permanently redirected by the app itself, always on. The old `EnableBackwardCompatibility` setting and the `site-redirect/` companion IIS app from earlier ASP.NET Core releases are gone entirely along with IIS.

PowerShell 7 does **not** need a separate install on the server — the PowerShell 7 engine used by WebJEA to run scripts ships inside the site folder and is hosted in-process. `Deploy.ps1` itself runs on **either edition**: it declares `#Requires -Version 5.1`, so the in-box Windows PowerShell on any supported Windows Server is enough, and it runs identically under `pwsh` if you have it. It must be elevated (`#Requires -RunAsAdministrator`).

`Deploy.ps1` has **no prerequisites of its own** — no PowerShell Gallery access, no DSC resource modules, and no WinRM configuration. It configures the server with in-box tooling only (`robocopy`, `sc.exe`, `secedit.exe`, `setspn.exe`, the firewall cmdlets). Earlier alpha builds installed the NuGet provider, PowerShellGet and three gallery DSC modules first; that whole step is gone, along with the `-OnlySections PowerShell` value that selected it.

### Settings schema changes (from the IIS-era settings file)

| Old key | New key | Notes |
|---|---|---|
| `SiteName`, `AppPoolName` | `ServiceName` | Windows service name; defaults to `WebJEA` |
| `AppPoolUserName` | `ServiceUserName` | gMSA ⇒ trailing `$`, no password; otherwise pass the password with `-ServicePassword` |
| `AppPoolPassword` | `-ServicePassword` parameter | Passwords are never stored in the settings file. A non-empty `ServicePassword` key is rejected; an empty one (from earlier alpha templates) is ignored |
| `AppPoolLoadUserProfile` | *(removed)* | Not applicable to a Windows service |
| `DisableDefaultWebsite` | *(removed)* | No IIS site to disable |
| *(new)* | `HttpPort`, `HttpsPort` | Port number to listen on; `0` or omitted disables that listener. At least one is required; `HttpsPort` requires `CertSubject` or `CertPath` |
| `CertThumbprint` | `CertSubject` | Kestrel selects the certificate by subject (the match that expires last wins), so renewals no longer need a settings change. A file with `CertThumbprint` is rejected; when that thumbprint is still in the store, the error names its subject to use |
| *(new)* | `CertPath` | Alternative to `CertSubject`: full path to a PFX file, reloaded by Kestrel when it changes — for ACME certificates. Password via `-CertPassword`. See [Certificate Renewal](Certificate-Renewal.md) |
| `RedirectPort80To443` | *(removed)* | HTTP is redirected to HTTPS automatically whenever both `HttpPort` and `HttpsPort` are set; the redirect is no longer independently settable |
| `SiteFQDN`, `SecondarySiteFQDNs` | `SiteFQDNs` | One array holding every host name WebJEA answers on; there is no primary/secondary distinction (all names were already treated identically for allowed hosts, SPNs, certificate coverage and the smoke probe). A single name may also be given as a plain string. Each entry is validated as a host name — no scheme, port, path or wildcard |
| `AppName`, `ParentSiteName` | *(removed)* | Sub-application installs are no longer supported on Windows — see below |

A settings file that still contains any retired key (`SiteName`, `AppPoolName`, `AppPoolUserName`, `AppPoolPassword`, `AppPoolLoadUserProfile`, `DisableDefaultWebsite`, `AppName`, `ParentSiteName`, `SiteFQDN`, `SecondarySiteFQDNs`, `CertThumbprint`) is rejected outright with an error naming the offending keys — Deploy.ps1 does not attempt a partial or best-effort translation of an old file.

### Migrating an existing IIS install

1. Stop and remove the old WebJEA IIS site and app pool(s) (`Remove-Website`, `Remove-WebAppPool`), or at minimum free up ports 80/443 so the new service can bind them.
2. Your existing `ScriptsPath` and log files are reused as-is — nothing to change there. The configuration file is now named `webjea.json` instead of `config.json`: Deploy.ps1 renames an existing `config.json` in `ScriptsPath` for you (it never overwrites a `webjea.json` that is already there), and WebJEA itself falls back to a `config.json` beside the configured file, logging a warning, if `webjea.json` is missing.
3. Convert your settings file to the new schema (see the table above) and run `Deploy.ps1` against it.
4. Deploy.ps1 refuses to run while IIS has site bindings on the ports your settings request (or when the `W3SVC` service exists but its bindings can't be verified), so it can't half-migrate a server out from under a live IIS install. IIS serving unrelated sites on other ports is fine and does not block the deployment. Pass `-IgnoreExistingIIS` to skip the check entirely once you've confirmed any remaining conflict is resolved.
5. Once the new install is verified, the old site folder can be deleted.

### Sub-application installs are no longer supported

The previous sub-application model — installing additional WebJEA instances into subfolders of a site, each with its own app pool and credential (`settings-subapp.template.jsonc`) — was removed along with IIS hosting. Each former sub-app now runs as its own **container instance** behind a reverse proxy; see [docker.md — Multiple instances (sub-sites) and scaling](docker.md#multiple-instances-sub-sites-and-scaling). If you're not ready to move to containers, stay on the previous WebJEA release until you are.

## What no longer applies (vs. the WebForms releases)

- In-place upgrade guidance: replace the `site/` contents but keep your `appsettings.Production.json` and `NLog.config`. Upgrading from a WebForms (pre-.NET 10) release is **not** an in-place upgrade — deploy fresh with Deploy.ps1 and point it at your existing scripts folder.
- **Breaking change:** the app no longer reads `WebJEA:HttpPort`, `WebJEA:HttpsPort` or `WebJEA:CertThumbprint` — listeners and the HTTPS certificate are Kestrel's native `Kestrel:Endpoints` configuration, the same mechanism containers use. WebJEA refuses to start while any of the old keys is present in its configuration. Re-running `Deploy.ps1` migrates `appsettings.Production.json` (writes `Kestrel:Endpoints`, removes the old keys); in the settings file, replace `CertThumbprint` with `CertSubject` (see the schema table above).
- **Breaking change:** passwords are no longer read from the settings file. Pass a regular (non-gMSA) service account's password with `-ServicePassword`; a settings file with a non-empty `ServicePassword` is rejected. Re-deploys of an existing service with the same account don't need it — the stored credential is kept.
- Certificate trust is no longer enforced: an untrusted, expired or name-mismatched certificate is a warning, and Kestrel is always allowed to serve it. The short-lived `AllowUntrustedCert` setting from earlier alphas is ignored with a warning; remove it from your settings file.
