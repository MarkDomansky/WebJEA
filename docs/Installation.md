# Installation

WebJEA ships with `Deploy.ps1`, which installs and configures a self-contained Windows Server deployment: WebJEA runs as a Kestrel-backed **Windows service** — IIS is not installed or used, and no .NET runtime needs to be installed on the server. The steps below get you to a working configuration.

For Docker, or migrating an existing IIS install, see the [deployment guide](deployment-migration.md) and [docker.md](docker.md).

## Typical/Recommended installation (Windows)

1. [Download](https://github.com/markdomansky/WebJEA/releases) the release zip and extract it on the server. `Deploy.ps1` and `settings.template.json` are in the root of the zip.
2. Get a certificate covering the host name(s) users will browse to, ideally from a CA your users' browsers trust, and decide where WebJEA reads it from — see [Certificate Renewal](Certificate-Renewal.md):
   * **A PFX file** (`CertPath`) — best for ACME certificates (Let's Encrypt and others) that renew every month or two: renewals are picked up without a restart. Put it in a folder of its own, e.g. `C:\webjea\tls\webjea.pfx` — see [where to put the PFX](Certificate-Renewal.md#where-to-put-the-pfx).
   * **The machine store** (`CertSubject`) — best for internal CA certificates: import it into `LocalMachine\My` **with its private key** and note its subject (e.g. `CN=webjea.example.com` → `webjea.example.com`).

   Either way you don't adjust permissions yourself — `Deploy.ps1` grants the service account read access to the key or the PFX's folder, which is what lets that account stay a non-administrator. *Optional but strongly recommended — see [System Requirements](System-Requirements.md#certificate-recommended).*
3. Create a group Managed Service Account (gMSA) for the service to run as — see [Creating a gMSA](#creating-a-gmsa) below. A standard AD user account with a password also works.
4. Copy `settings.template.json` to e.g. `settings.json` and fill it in — every setting is described in the [settings reference](#settings-reference) below.
5. From an elevated prompt — the in-box **Windows PowerShell 5.1** (`powershell.exe`) or **PowerShell 7** (`pwsh`), either works — run:

   ```powershell
   .\Deploy.ps1 -SettingsFile .\settings.json
   ```

   Add `-TestOnly` to see what would change without changing it, or restrict the run with `-OnlySections Server,Service,WebJEA,Finalize`. If `HttpPort` is enabled without `HttpsPort` — an intentional HTTP-only install — add `-AllowHttpWithoutRedirect` to confirm HTTP will be served with no redirect to HTTPS.

   Passwords are parameters, never settings — the settings file can't hold them:

   * `-ServicePassword` — the password of a regular (non-gMSA) `ServiceUserName`. Needed on the first deploy and when the account or its password changes; later deploys keep the stored credential.
   * `-CertPassword` — the password of a password-protected PFX at `CertPath`. Later deploys reuse the stored one.

   ```powershell
   .\Deploy.ps1 -SettingsFile .\settings.json -ServicePassword (Read-Host -AsSecureString 'Service account password')
   ```

6. Browse to `https://<one of your SiteFQDNs>/`. By default, machine local administrators have access to the starter page, which executes a demo script exercising the major features of WebJEA so you can verify everything works.

`Deploy.ps1` copies the site files, creates and starts the Windows service, writes the production configuration, grants the service account the [folder access](System-Requirements.md#folder-permissions) it needs and read access to the certificate (its private key, or the PFX's folder), opens firewall ports, verifies Kerberos SPNs, and smoke-tests every configured binding — the [deployment guide](deployment-migration.md) describes each step in detail. It warns, without changing anything, when the site, scripts or log folder is open to every user on the server.

While not strictly required, running Windows updates after deployment is always a good idea.

## Settings reference

The settings file is consumed by the installer (`Deploy.ps1`). It uses JSON syntax.

| Setting | Description |
|---|---|
| `ServiceName` | The Windows service name to create. Default `WebJEA`. |
| `SitePath` | Where to place the application files — the release zip's `site` folder is copied here. |
| `SiteFQDNs` | Every host name WebJEA answers on, as an array — the name users browse to plus any aliases or load-balanced name. Requests for other names get HTTP 400. Each name must be covered by the certificate (subject or SAN, wildcard SANs count) when `HttpsPort` is enabled, must resolve to this server in DNS, and — for Kerberos — must have an `HTTP/<name>` SPN registered to the service account (`Deploy.ps1` checks and registers these). Wildcards are not accepted; spell each name out. For cert-less test installs without DNS, the machine name works. A single name may also be given as a plain string. |
| `ServiceUserName` | The account the WebJEA Windows service runs as. We strongly recommend a gMSA (trailing `$`, no password); see [Creating a gMSA](#creating-a-gmsa). **Scripts execute as this account.** Alternately, use a standard domain or local account and pass its password with the `-ServicePassword` parameter — there is no password setting. |
| `HttpPort` / `HttpsPort` | Ports. Set a port number to listen, or `0`/omit to disable that listener. At least one must be enabled. `HttpsPort` requires `CertSubject` or `CertPath`. When both are set, HTTP is automatically and permanently redirected to HTTPS — there is no separate setting for this. Kestrel binds these ports exclusively, unlike IIS's shared `http.sys` binding — nothing else on the server, including IIS, can also be listening on the same port. |
| `CertSubject` | **Machine-store certificate.** Text contained in the subject of the HTTPS certificate in `LocalMachine\My` — usually the host name in its CN (`webjea.example.com` for `CN=webjea.example.com`). It is matched as a case-insensitive substring of the subject, not the SANs, and when several certificates match, Kestrel serves the one that expires last — so a renewed certificate is picked up on the next service restart with no settings change. `Deploy.ps1` grants `ServiceUserName` read access to the private key of every matching certificate ([details](System-Requirements.md#private-key-permissions)); a later renewal needs that grant again ([Certificate Renewal](Certificate-Renewal.md#store-mode-certsubject)). Set this or `CertPath`, not both. |
| `CertPath` | **PFX file certificate.** Full path to a `.pfx` file, in a folder of its own ([where to put it](Certificate-Renewal.md#where-to-put-the-pfx)). Kestrel reloads the file whenever it changes, so an ACME client renewing it in place needs no restart and no permission changes. `Deploy.ps1` grants `ServiceUserName` read access to the file's folder. For a password-protected PFX, pass the password with the `-CertPassword` parameter. Set this or `CertSubject`, not both. |
| `ScriptsPath` | Where the WebJEA scripts (and `webjea.json`) will be placed. If the folder already has any files, the starter-script copy is skipped to avoid overwriting customizations. |
| `LogPath` / `LogFile` | Folder and file name for the application log. |
| `LogUsageFile` | Simplified usage log, importable into Excel or other analysis tools. |

`Deploy.ps1` turns the port and certificate settings into Kestrel's native endpoint configuration — `Kestrel:Endpoints` in the site's `appsettings.Production.json`, with the certificate as `Subject`/`Store` (always `AllowInvalid: true`) or `Path` (plus `Password` for a protected PFX) — and owns that section: hand edits to `Kestrel:Endpoints` are overwritten on the next deploy. Other `Kestrel` settings (for example `Kestrel:Limits`) are left alone.

## Creating a gMSA

If your domain has never used gMSAs, create the KDS root key first (once per forest):

```powershell
# Once per forest; the -10 hours trick makes the key usable immediately in a lab.
# In production, run without it and wait up to 10 hours for AD replication.
Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))
```

Then create the account and install it on the WebJEA server:

```powershell
# On a machine with the AD PowerShell module; WEBJEA1 is the server that runs WebJEA
New-ADServiceAccount -Name gmsa1 -DNSHostName gmsa1.domain1.local `
    -PrincipalsAllowedToRetrieveManagedPassword WEBJEA1$

# On the WebJEA server
Install-ADServiceAccount gmsa1
```

Grant the gMSA whatever permissions your scripts will need (start with none and add as required). In your settings file, use `"ServiceUserName": "domain1\\gmsa1$"`, and don't pass `-ServicePassword`.

## Next steps

* [Usage](Usage.md) — how `webjea.json` and commands work
* [Windows authentication](windows.md) — Kerberos SPNs and AD group authorization
* [Entra ID authentication](entra.md) — cloud or non-domain deployments
* [PowerShell 7 compatibility](powershell7.md) — before pointing WebJEA at existing scripts
