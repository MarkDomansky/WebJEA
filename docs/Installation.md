# Installation

WebJEA ships with `Deploy.ps1`, which installs and configures a complete
IIS-hosted deployment: it installs the Windows features and DSC modules it needs,
creates the IIS site and application pool, copies the site files, lays down a
starter configuration, and optionally configures HTTPS.

`Deploy.ps1` is driven by a settings file — you do not edit the script itself.
`settings.template.jsonc` ships alongside it as a starting point.

## Typical/Recommended installation

1. [Download](https://github.com/markdomansky/WebJEA/releases) the release zip and
   extract it on the server, e.g. to `C:\Source`. `Deploy.ps1`,
   `settings.template.jsonc` and the `site` folder are in the root of the zip;
   `Deploy.ps1` deploys the `site` folder sitting next to it, so keep them
   together.
2. Create an SSL certificate covering the FQDN users will browse to, import it
   into the `LocalMachine\My` store **with its private key**, and note the
   thumbprint (no spaces, no hidden characters). *Optional but strongly
   recommended — see
   [System Requirements](System-Requirements.md#certificate-recommended).*
3. Create a group Managed Service Account (gMSA) for the application pool to run
   as — see [Creating a gMSA](#creating-a-gmsa) below. A standard AD user account
   with a password also works.
4. Copy `settings.template.jsonc` to e.g. `settings.jsonc` and fill it in — every
   setting is described in the [settings reference](#settings-reference) below.
5. From an **elevated** PowerShell prompt on the target server, run:

   ```powershell
   .\Deploy.ps1 -SettingsFile .\settings.jsonc
   ```

   Add `-TestOnly` to see what would change without changing it, or restrict the
   run to part of the deployment with `-OnlySections` (see
   [Deploy.ps1 options](#deployps1-options)).

While not strictly required, a reboot is recommended after deployment, as is
running Windows Update — several web components are installed during the run.

After the reboot, browse to `https://<your SiteFQDN>/`. By default, machine local
administrators have access to the default page. It should execute and display a
form demonstrating all of the major features of WebJEA.

## Settings reference

These are the keys in `settings.template.jsonc`. The file is JSON with comments
(`jsonc`) — set your editor's language mode to JSONC so the comments render
correctly.

| Setting | Description |
| ------- | ----------- |
| `SiteName` | Name of the IIS site to create. |
| `SitePath` | Folder to create the IIS site in, e.g. `C:\inetpub\webjea`. The `site` folder from the release is copied here. |
| `SiteFQDN` | Binding FQDN for the website. Must match the name on your SSL certificate. For HTTP only, `*` is accepted. |
| `AppPoolName` | Name of the IIS application pool to create. |
| `AppPoolUserName` | Application pool identity. A gMSA (trailing `$`, empty password) is strongly recommended; see [Creating a gMSA](#creating-a-gmsa). **Scripts execute as this account.** |
| `AppPoolPassword` | Password for `AppPoolUserName`. Leave empty for a gMSA. Storing a password here is one more reason to prefer a gMSA. |
| `AppPoolLoadUserProfile` | Must be `true` if your scripts create and import remote PSSessions. `false` is marginally more secure. |
| `ScriptsPath` | Where WebJEA's scripts and `config.json` are placed, e.g. `c:\webjea`. **If the folder already contains files, this step is skipped** so your customizations are never overwritten. |
| `LogPath` | Folder for WebJEA's log files. |
| `LogFile` | Application log file name, e.g. `webjea.log`. |
| `LogUsageFile` | Simplified usage log, e.g. `webjea-usage.log` — a flat format suited to importing into Excel or another analysis tool. |
| `CertThumbprint` | Thumbprint of a certificate in the `LocalMachine\My` store covering `SiteFQDN`, imported with its private key. **If empty or invalid, HTTPS is not configured and the site is served over HTTP.** |
| `RedirectPort80To443` | Redirect HTTP to HTTPS. Only applied when `CertThumbprint` is set and valid. |
| `EnableBackwardCompatibility` | Adds a URL Rewrite rule issuing a 301 from `/WebJEA/*` to the root path. Needed only when migrating from an older release that was installed as a `/WebJEA` sub-application; leave `false` for a fresh install. |
| `DisableDefaultWebsite` | Disables the IIS Default Web Site. Recommended — it usually listens on port 80 and will otherwise conflict with the WebJEA site. Set `false` to keep it, and change the port on one of the two. |

## Deploy.ps1 options

| Parameter | Purpose |
| --------- | ------- |
| `-SettingsFile <path>` | **Required.** The settings file described above. |
| `-TestOnly` | Evaluate every step and report what would change, without changing anything. Run this first. |
| `-OnlySections <names>` | Restrict the run to one or more of `PowerShell`, `Server`, `WebServer`, `WebJEA`, `Finalize` (default `All`). |
| `-OnlyReturnSteps` | Print the deployment steps without executing them. |
| `-OnlyReturnSettings` | Print the resolved settings without executing anything — handy for confirming the file parsed as you expect. |

The sections run in order:

| Section | Does |
| ------- | ---- |
| `PowerShell` | NuGet package provider, WinRM, and the DSC resource modules the rest of the run depends on. |
| `Server` | Windows/IIS features, URL Rewrite, **copies the `site` folder to `SitePath`**, and seeds `ScriptsPath` (skipped if that folder already has files). |
| `WebServer` | The IIS site, application pool and identity, bindings, certificate and HTTP→HTTPS redirect. |
| `WebJEA` | Sets `basePath` in `config.json` to your `ScriptsPath`. |
| `Finalize` | Restarts IIS so every change takes effect. |

## Creating a gMSA

If your domain has never used gMSAs, create the KDS root key first (once per
forest). The `-EffectiveTime` in the past makes the key usable immediately
instead of after the normal 10-hour replication wait — acceptable in a
single-DC or lab domain, but in production prefer `Add-KdsRootKey -EffectiveImmediately`
and wait out the interval:

```powershell
Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))
```

Then create the account and allow the web server to retrieve its password:

```powershell
New-ADServiceAccount -Name gmsa1 `
    -DNSHostName gmsa1.domain1.local `
    -PrincipalsAllowedToRetrieveManagedPassword webserver1$

# Run this on the web server itself:
Install-ADServiceAccount gmsa1
```

Grant the gMSA whatever permissions your scripts will need — start with none and
add as required. In your settings file, use
`"AppPoolUserName": "domain1\\gmsa1$"` with an empty `AppPoolPassword`.

## Installing to a sub-folder

There is no technical requirement for WebJEA to be installed as its own IIS site.
`Deploy.ps1` creates one because that is the configuration it can set up
reliably; if you need WebJEA under an existing site instead, create the virtual
directory and application yourself and point it at the deployed `site` folder.
That arrangement is outside what `Deploy.ps1` manages.

If you are moving *from* such a sub-application layout (`/WebJEA`) to a dedicated
site, set `EnableBackwardCompatibility` to `true` so existing bookmarks and links
in ticketing systems keep working.

## What's in the release

* The compiled WebJEA site files (`site`)
* The deployment script (`Deploy.ps1`)
* The settings template (`settings.template.jsonc`)
* Starter configuration and demo scripts

## Upgrading

In-place upgrades are straightforward: drop the new `site` files over the
deployed site folder. Changes take effect immediately; no `iisreset` is required.

**Back up your configuration first.** The site folder holds `web.config` and
`nlog.config`, which you may well have customized, and a file copy replaces them
with the release's versions. Your `config.json` is safe as long as it lives in
`ScriptsPath` outside the site folder, which is where `Deploy.ps1` puts it.

Re-running `Deploy.ps1` is also supported, with the same caveat and one more: the
site-copy step deliberately has no checksum test, so it **always overwrites** the
site folder, and it belongs to the `Server` section (not `WebJEA`). To refresh
only the site files:

```powershell
.\Deploy.ps1 -SettingsFile .\settings.jsonc -OnlySections Server,Finalize
```

Re-apply your `web.config` and `nlog.config` changes afterwards.
