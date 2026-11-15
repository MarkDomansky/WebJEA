# System Requirements

The system requirements are pretty flexible and will depend on usage.

## Operating system

* **Windows Server** (2019 or later recommended) for the classic domain-joined deployment with Windows (Kerberos/NTLM) authentication. WebJEA installs as a self-hosted **Windows service** running on Kestrel — no IIS role is required or used, and the release is a self-contained .NET package, so there is no .NET runtime prerequisite to install.
* **Docker** images for Linux containers can be built from the included configurations — see [docker.md](docker.md).

## PowerShell

Nothing to install: WebJEA hosts **PowerShell 7** in-process, and the engine ships inside the site folder. Scripts execute under PowerShell 7 — see [powershell7.md](powershell7.md) for the (small) compatibility differences from Windows PowerShell 5.1. `Deploy.ps1` itself is *run from* either edition — the in-box **Windows PowerShell 5.1** (`powershell.exe`) or **PowerShell 7** (`pwsh`) — elevated. It needs no PowerShell Gallery access, DSC modules or WinRM configuration; it uses in-box tooling only.

## Service account

We strongly recommend a **gMSA** (group Managed Service Account) to run the WebJEA Windows service. A standard user account with a password also works — set `ServiceUserName`, and pass the password to `Deploy.ps1` with `-ServicePassword` (passwords are never stored in the settings file). Scripts execute in the context of this account, so grant it only the permissions your scripts need. For Windows authentication mode, the account must be able to read AD to resolve group membership ([windows.md](windows.md)).

**The service account does not need to be a local administrator.** The one thing that historically pushed people into granting it admin rights is the HTTPS certificate's private key; `Deploy.ps1` handles that directly — see below.

## Certificate (recommended)

A certificate is not technically required (HTTP-only installs are possible), but if you pass sensitive data to or from users you may be exposing it to untrusted networks. WebJEA reads the HTTPS certificate from one of two places — a **PFX file** (`CertPath`, best for ACME certificates that renew every month or two) or the **`LocalMachine\My` store** (`CertSubject`, best for internal CA certificates). [Certificate Renewal](Certificate-Renewal.md) compares them and covers renewing each.

Kestrel can only serve a certificate that:

* has its **private key** — a public-key-only certificate cannot complete a TLS handshake; and
* allows **Server Authentication** (or has no enhanced key usage restriction at all).

`Deploy.ps1` checks the certificate before it changes anything and stops if either is missing, or if a PFX can't be opened. Everything else is your call, so `Deploy.ps1` **warns and carries on** when the certificate:

* is not **trusted by this server** — its chain doesn't build to a root in the machine's trust store, or its revocation status can't be checked. Certificates from an internal enterprise CA are trusted once that CA's chain is installed, which domain-joined servers normally receive through Group Policy;
* is out of date, or expires within 30 days; or
* doesn't cover every host name in `SiteFQDNs` in its subject or SANs (wildcard SANs count).

Browsers will complain about each of these, so a warning usually means something to fix — but a lab or self-signed certificate deploys without any extra setting.

### How the certificate is selected

WebJEA uses Kestrel's built-in certificate configuration, written to `Kestrel:Endpoints:Https:Certificate`:

* **`CertPath`** is written as `Path` (plus `Password` for a protected PFX). Kestrel loads that file, and loads it again whenever the file changes — so a renewal written to the same path is served to new connections without a restart.
* **`CertSubject`** is written as `Subject`, and Kestrel picks the certificate itself every time the service starts. It looks for certificates whose **subject contains** that text (case-insensitive; SANs are not searched), skips any without Server Authentication or a private key it can open, then serves the one with the **latest expiry date**. `AllowInvalid` is always set, so an untrusted certificate is still served, and — more importantly — an unreachable revocation server at startup can't stop HTTPS from starting.

With `CertSubject`, a renewed certificate with the same subject takes over on the next service restart, without any settings change. Because the latest expiry wins regardless of trust, a self-signed certificate imported with the same subject would be served over a valid one; `Deploy.ps1` warns when that is the case. Keep `CertSubject` specific enough to match only the certificates you intend: `example.com` would also match `CN=intranet.example.com`. `Deploy.ps1` also warns when the matches have different subjects.

### Private key permissions

**With `CertPath`**, the service account needs to read the PFX file. `Deploy.ps1` grants it **Read** on the PFX's folder — the folder rather than the file, because a renewal replaces the file and the new one takes its permissions from the folder. Nothing else about the folder is changed; see [where to put the PFX](Certificate-Renewal.md#where-to-put-the-pfx) for locking it down.

**With `CertSubject`**, putting the certificate in `LocalMachine\My` is not by itself enough for a non-admin service account. The store's contents are readable by everyone, but the *private key file* behind the certificate is created with permissions for `SYSTEM` and the local `Administrators` group only. If the service account cannot read that file, Kestrel cannot open the key and the HTTPS listener fails at startup — which is why running the service as an administrator "fixes" it.

`Deploy.ps1` grants the service account plain **Read** on that file as part of the Service section, so the account can stay an ordinary user. It does this for **every** certificate matching `CertSubject`, not just the one served today. To do it by hand, or to check what is currently granted: `certlm.msc` → the certificate → **All Tasks** → **Manage Private Keys** → Add. For a gMSA, click **Object Types** and tick *Service Accounts* first, or the account will not be found.

> [!IMPORTANT]
> **On renewal (`CertSubject`):** a renewed certificate normally has a **new** private key file, which starts out with the default admins-only permissions again. Grant the service account read on it **before** the service next restarts — Kestrel selects the renewed certificate on restart, and the HTTPS listener fails if the account cannot read its key. [Certificate Renewal](Certificate-Renewal.md#store-mode-certsubject) has a script for the grant and restart. A `CertPath` PFX needs none of this.

Certificates whose keys live in a TPM, HSM, or smart card have no file to permission this way. `Deploy.ps1` warns and moves on; grant access with that provider's own tooling.

The [Entra](entra.md) **Graph client certificate** gets the same treatment when the settings file names it. With `GraphCertSubject`, `Deploy.ps1` grants Read on the private key of every certificate with that exact subject. With `GraphCertPath`, it grants Read on the PFX's folder, which can be the same folder as `CertPath`. See [Certificate Renewal](Certificate-Renewal.md#graph-client-certificate).

## CPU / RAM

There are no firm requirements beyond the Microsoft recommendations for your Windows Server version — usage drives requirements. Expect roughly the cost of spinning up a PowerShell 7 runspace per execution plus typical ASP.NET Core consumption. WebJEA has been demonstrated to work on 1 CPU and 1 GB of RAM and runs reliably on 2 CPU with 4 GB of RAM; what your scripts do will dominate.

## Disk

The application itself is a few hundred MB (self-contained .NET plus the embedded PowerShell engine). Beyond that, disk usage is driven by your scripts and log files.

### Folder permissions

The service account needs this access to the folders in the settings file:

| Folder | Access | Why |
|---|---|---|
| `SitePath` | Read & execute | Runs `WebJEA.exe` and reads its configuration. WebJEA writes nothing here. |
| `ScriptsPath` | Read & execute | Reads `webjea.json` and the scripts. Scripts that write files need more, on the folders they write to. |
| `LogPath` | Modify | Creates, appends to and archives the log files. |

`Deploy.ps1` grants only what is missing, and never removes an entry or changes inheritance. Access the account already has through Everyone, Authenticated Users, Users or Service counts, so a default install usually gets a new entry on `LogPath` only.

Because `Deploy.ps1` leaves your permissions alone, it **warns** when one of these folders is open to everyone on the server instead:

* **Write access for a broad group** (Everyone, Authenticated Users, Users, Interactive, Guests, Domain Users) on any of the three. Anyone who can sign in to the server could then change what WebJEA runs: drop a DLL next to `WebJEA.exe`, or edit a script that runs as the service account. Folders created under `C:\` get this by default: Windows Server lets Users create files in subfolders, and Windows 10/11 gives Authenticated Users Modify.
* **Read access for a broad group** on `SitePath`, once `appsettings.Production.json` holds a secret (`AzureAd:ClientSecret`, the password of a `CertPath` or `GraphCertPath` PFX, or a whole PFX in a `ClientCredentials` entry).

To restrict a folder, turn off inheritance and grant only SYSTEM, Administrators and the service account. The well-known SIDs make this work on non-English Windows too:

```powershell
$account = 'DOMAIN\gmsa1$'
$sid = ([System.Security.Principal.NTAccount]$account).Translate([System.Security.Principal.SecurityIdentifier]).Value
$folders = [ordered]@{ 'C:\webjea\site' = 'RX'; 'C:\webjea\scripts' = 'RX'; 'C:\webjea\logs' = 'M' }
foreach ($folder in $folders.GetEnumerator())
{
    # SYSTEM and Administrators get full control; the service account gets what the table above lists
    icacls $folder.Key /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' "*${sid}:(OI)(CI)($($folder.Value))"
}
```

> [!NOTE]
> `/inheritance:r` removes every entry the folder inherited, including access you granted on a parent folder such as `C:\webjea`. Entries added directly to the folder, such as a group of script authors on `ScriptsPath`, are kept. Grant anything else on the folder itself; `Deploy.ps1` leaves those entries alone.
