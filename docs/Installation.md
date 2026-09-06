# Installation

WebJEA ships with `Deploy.ps1`, which installs and configures a self-contained
Windows Server deployment: WebJEA runs as a Kestrel-backed **Windows service** — IIS
is not installed or used, and no .NET runtime needs to be installed on the server.
The steps below get you to a working configuration.

For Linux hosting, Docker, or migrating an existing IIS install, see the
[deployment guide](deployment-migration.md) and [docker.md](docker.md).

## Typical/Recommended installation (Windows)

1. [Download](https://github.com/markdomansky/WebJEA/releases) the release zip and
   extract it on the server. `Deploy.ps1` and `settings.template.json` are in the
   root of the zip.
2. Create an SSL certificate covering the host name(s) users will browse to, import
   it into the `LocalMachine\My` store **with its private key**, and note the
   thumbprint (no spaces, no hidden characters). You do not need to adjust the key's
   permissions yourself — `Deploy.ps1` grants the service account read access to it,
   which is what lets that account stay a non-administrator. *Optional but strongly
   recommended — see
   [System Requirements](System-Requirements.md#certificate-recommended).*
3. Create a group Managed Service Account (gMSA) for the service to run as — see
   [Creating a gMSA](#creating-a-gmsa) below. A standard AD user account with a
   password also works.
4. Copy `settings.template.json` to e.g. `settings.json` and fill it in — every
   setting is described in the [settings reference](#settings-reference) below.
5. From an elevated **PowerShell 7** (`pwsh`) prompt — not Windows PowerShell 5.1 —
   run:

   ```powershell
   .\Deploy.ps1 -SettingsFile .\settings.json
   ```

   Add `-TestOnly` to see what would change without changing it, or restrict the run
   with `-OnlySections PowerShell,Server,Service,WebJEA,Finalize`. If `HttpPort` is
   enabled without both `HttpsPort` and `CertThumbprint` set — an intentional
   HTTP-only install — add `-AllowHttpWithoutRedirect` to confirm HTTP will be served
   with no redirect to HTTPS.
6. Browse to `https://<one of your SiteFQDNs>/`. By default, machine local administrators have
   access to the starter page, which executes a demo script exercising the major
   features of WebJEA so you can verify everything works.

`Deploy.ps1` copies the site files, creates and starts the Windows service, writes the
production configuration, grants the service account read access to the certificate's
private key, opens firewall ports, verifies Kerberos SPNs, and smoke-tests every
configured binding — the [deployment guide](deployment-migration.md) describes each
step in detail.

While not strictly required, running Windows updates after deployment is always a
good idea.

## Settings reference

The settings file is consumed by the installer (`Deploy.ps1`). It uses JSON syntax.

| Setting | Description |
|---|---|
| `ServiceName` | The Windows service name to create. Default `WebJEA`. |
| `SitePath` | Where to place the application files — the release zip's `site` folder is copied here. |
| `SiteFQDNs` | Every host name WebJEA answers on, as an array — the name users browse to plus any aliases or load-balanced name. Requests for other names get HTTP 400. Each name must be covered by the certificate (subject or SAN, wildcard SANs count) when `HttpsPort` is enabled, must resolve to this server in DNS, and — for Kerberos — must have an `HTTP/<name>` SPN registered to the service account (`Deploy.ps1` checks and registers these). Wildcards are not accepted; spell each name out. For cert-less test installs without DNS, the machine name works. A single name may also be given as a plain string. |
| `ServiceUserName` | The account the WebJEA Windows service runs as. We strongly recommend a gMSA (trailing `$`, empty password); see [Creating a gMSA](#creating-a-gmsa). **Scripts execute as this account.** Alternately, use a standard domain or local account with `ServicePassword` set. |
| `ServicePassword` | Password for `ServiceUserName`. Leave empty for a gMSA. |
| `HttpPort` / `HttpsPort` | Ports. Set a port number to listen, or `0`/omit to disable that listener. At least one must be enabled. `HttpsPort` requires `CertThumbprint`. When `HttpPort`, `HttpsPort`, and `CertThumbprint` are all set, HTTP is automatically and permanently redirected to HTTPS — there is no separate setting for this. Kestrel binds these ports exclusively, unlike IIS's shared `http.sys` binding — nothing else on the server, including IIS, can also be listening on the same port. |
| `CertThumbprint` | Thumbprint of a certificate in the `LocalMachine\My` store covering every FQDN above, imported with its private key. `Deploy.ps1` grants `ServiceUserName` read access to that key so the account does not need to be a local administrator; re-run it after a renewal, since a new key file starts out admins-only ([details](System-Requirements.md#private-key-permissions)). Leave the placeholder (or empty) only when `HttpsPort` is `0`. |
| `ScriptsPath` | Where the WebJEA scripts (and `config.json`) will be placed. If the folder already has any files, the starter-script copy is skipped to avoid overwriting customizations. |
| `LogPath` / `LogFile` | Folder and file name for the application log. |
| `LogUsageFile` | Simplified usage log, importable into Excel or other analysis tools. |

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

Grant the gMSA whatever permissions your scripts will need (start with none and add
as required). In your settings file, use `"ServiceUserName": "domain1\\gmsa1$"` with
an empty `ServicePassword`.

## Next steps

* [Usage](Usage.md) — how `config.json` and commands work
* [Windows authentication](windows.md) — Kerberos SPNs and AD group authorization
* [Entra ID authentication](entra.md) — cloud or non-domain deployments
* [PowerShell 7 compatibility](powershell7.md) — before pointing WebJEA at existing scripts
