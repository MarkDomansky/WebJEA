# System Requirements

The system requirements are pretty flexible and will depend on usage.

## Operating system

* **Windows Server** (2019 or later recommended) for the classic domain-joined
  deployment with Windows (Kerberos/NTLM) authentication. WebJEA installs as a
  self-hosted **Windows service** running on Kestrel — no IIS role is required or
  used, and the release is a self-contained .NET package, so there is no .NET runtime
  prerequisite to install.
* **Linux** (Debian/RHEL and compatible) is supported when using
  [Entra ID authentication](entra.md) — see the
  [deployment guide](deployment-migration.md#linux-debian--rhel).
* **Docker** images for Linux and Windows containers can be built from the included
  configurations — see [docker.md](docker.md).

## PowerShell

Nothing to install: WebJEA hosts **PowerShell 7** in-process, and the engine ships
inside the site folder. Scripts execute under PowerShell 7 — see
[powershell7.md](powershell7.md) for the (small) compatibility differences from
Windows PowerShell 5.1. Note that `Deploy.ps1` itself must be *run from* PowerShell 7
(`pwsh`), elevated.

## Service account

We strongly recommend a **gMSA** (group Managed Service Account) to run the WebJEA
Windows service. A standard user account with a password also works — `Deploy.ps1`
supports both via the `ServiceUserName`/`ServicePassword` settings. Scripts execute in
the context of this account, so grant it only the permissions your scripts need. For
Windows authentication mode, the account must be able to read AD to resolve group
membership ([windows.md](windows.md)).

**The service account does not need to be a local administrator.** The one thing that
historically pushed people into granting it admin rights is the HTTPS certificate's
private key; `Deploy.ps1` handles that directly — see below.

## Certificate (recommended)

A certificate is not technically required (HTTP-only installs are possible), but if you
pass sensitive data to or from users you may be exposing it to untrusted networks.
Enabling the HTTPS listener requires a certificate in the `LocalMachine\My` store whose
subject or SANs cover every host name users browse to, imported **with its private
key** (a public-key-only certificate cannot complete a TLS handshake; `Deploy.ps1`
rejects one).

### Private key permissions

Putting the certificate in `LocalMachine\My` is not by itself enough for a non-admin
service account. The store's contents are readable by everyone, but the *private key
file* behind the certificate is created with permissions for `SYSTEM` and the local
`Administrators` group only. If the service account cannot read that file, Kestrel
cannot open the key and the HTTPS listener fails at startup — which is why running the
service as an administrator "fixes" it.

`Deploy.ps1` grants the service account plain **Read** on that file as part of the
Service section, so the account can stay an ordinary user. To do it by hand, or to
check what is currently granted: `certlm.msc` → the certificate → **All Tasks** →
**Manage Private Keys** → Add. For a gMSA, click **Object Types** and tick *Service
Accounts* first, or the account will not be found.

> **On renewal:** a renewed certificate normally has a **new** private key file, which
> starts out with the default admins-only permissions again. Re-run `Deploy.ps1` after
> updating `CertThumbprint` (or re-apply the grant manually) — otherwise the service
> keeps working until its next restart and then fails to start its HTTPS listener.

Certificates whose keys live in a TPM, HSM, or smart card have no file to permission
this way. `Deploy.ps1` warns and moves on; grant access with that provider's own
tooling.

## CPU / RAM

There are no firm requirements beyond the Microsoft recommendations for your Windows
Server version — usage drives requirements. Expect roughly the cost of spinning up a
PowerShell 7 runspace per execution plus typical ASP.NET Core consumption. WebJEA has
been demonstrated to work on 1 CPU and 1 GB of RAM and runs reliably on 2 CPU with
4 GB of RAM; what your scripts do will dominate.

## Disk

The application itself is a few hundred MB (self-contained .NET plus the embedded
PowerShell engine). Beyond that, disk usage is driven by your scripts and log files.
