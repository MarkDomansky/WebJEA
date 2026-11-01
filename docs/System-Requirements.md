# System Requirements

The system requirements are pretty flexible and will depend on usage.

## Operating system

* **Windows Server** (2019 or later recommended) for the classic domain-joined deployment with Windows (Kerberos/NTLM) authentication. WebJEA installs as a self-hosted **Windows service** running on Kestrel — no IIS role is required or used, and the release is a self-contained .NET package, so there is no .NET runtime prerequisite to install.
* **Docker** images for Linux containers can be built from the included configurations — see [docker.md](docker.md).

## PowerShell

Nothing to install: WebJEA hosts **PowerShell 7** in-process, and the engine ships inside the site folder. Scripts execute under PowerShell 7 — see [powershell7.md](powershell7.md) for the (small) compatibility differences from Windows PowerShell 5.1. `Deploy.ps1` itself is *run from* either edition — the in-box **Windows PowerShell 5.1** (`powershell.exe`) or **PowerShell 7** (`pwsh`) — elevated. It needs no PowerShell Gallery access, DSC modules or WinRM configuration; it uses in-box tooling only.

## Service account

We strongly recommend a **gMSA** (group Managed Service Account) to run the WebJEA Windows service. A standard user account with a password also works — `Deploy.ps1` supports both via the `ServiceUserName`/`ServicePassword` settings. Scripts execute in the context of this account, so grant it only the permissions your scripts need. For Windows authentication mode, the account must be able to read AD to resolve group membership ([windows.md](windows.md)).

**The service account does not need to be a local administrator.** The one thing that historically pushed people into granting it admin rights is the HTTPS certificate's private key; `Deploy.ps1` handles that directly — see below.

## Certificate (recommended)

A certificate is not technically required (HTTP-only installs are possible), but if you pass sensitive data to or from users you may be exposing it to untrusted networks. Enabling the HTTPS listener requires a certificate in the `LocalMachine\My` store that:

* is imported **with its private key** — a public-key-only certificate cannot complete a TLS handshake;
* covers every host name users browse to in its subject or SANs (wildcard SANs count);
* allows **Server Authentication** (or has no enhanced key usage restriction at all);
* is in date; and
* is **trusted by this server** — its chain builds to a root in the machine's trust store and its revocation status can be checked. Certificates from an internal enterprise CA qualify once that CA's chain is installed, which domain-joined servers normally receive through Group Policy.

`Deploy.ps1` checks all of these before it changes anything, and stops with the reason when one fails. For a lab or self-signed certificate, set `AllowUntrustedCert` to `true`: the trust check becomes a warning (and Kestrel is told to accept the certificate), while the other checks still apply.

### How the certificate is selected

WebJEA uses Kestrel's built-in certificate configuration: the `CertSubject` setting is written to `Kestrel:Endpoints:Https:Certificate:Subject`, and Kestrel picks the certificate itself every time the service starts. Kestrel looks for certificates whose **subject contains** that text (case-insensitive; SANs are not searched), skips any without Server Authentication or a private key — and, unless `AllowUntrustedCert` is set, any that are untrusted or out of date — then serves the one with the **latest expiry date**.

Because of that, a renewed certificate with the same subject takes over on the next service restart, without any settings change. Keep `CertSubject` specific enough to match only the certificates you intend: `example.com` would also match `CN=intranet.example.com`. `Deploy.ps1` warns when the matches have different subjects.

### Private key permissions

Putting the certificate in `LocalMachine\My` is not by itself enough for a non-admin service account. The store's contents are readable by everyone, but the *private key file* behind the certificate is created with permissions for `SYSTEM` and the local `Administrators` group only. If the service account cannot read that file, Kestrel cannot open the key and the HTTPS listener fails at startup — which is why running the service as an administrator "fixes" it.

`Deploy.ps1` grants the service account plain **Read** on that file as part of the Service section, so the account can stay an ordinary user. It does this for **every** certificate matching `CertSubject`, not just the one served today. To do it by hand, or to check what is currently granted: `certlm.msc` → the certificate → **All Tasks** → **Manage Private Keys** → Add. For a gMSA, click **Object Types** and tick *Service Accounts* first, or the account will not be found.

> [!IMPORTANT]
> **On renewal:** a renewed certificate normally has a **new** private key file, which starts out with the default admins-only permissions again. The settings file doesn't change, but after importing the renewed certificate, re-run `.\Deploy.ps1 -SettingsFile .\settings.json -OnlySections Service` (or re-apply the grant manually) **before** the service next restarts. Kestrel will select the renewed certificate on restart, and the HTTPS listener fails if the service account cannot read its key.

Certificates whose keys live in a TPM, HSM, or smart card have no file to permission this way. `Deploy.ps1` warns and moves on; grant access with that provider's own tooling.

## CPU / RAM

There are no firm requirements beyond the Microsoft recommendations for your Windows Server version — usage drives requirements. Expect roughly the cost of spinning up a PowerShell 7 runspace per execution plus typical ASP.NET Core consumption. WebJEA has been demonstrated to work on 1 CPU and 1 GB of RAM and runs reliably on 2 CPU with 4 GB of RAM; what your scripts do will dominate.

## Disk

The application itself is a few hundred MB (self-contained .NET plus the embedded PowerShell engine). Beyond that, disk usage is driven by your scripts and log files.
