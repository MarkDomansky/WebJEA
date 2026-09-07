# System Requirements

The system requirements are pretty flexible and will depend on usage.

## Operating system

**Windows Server, domain joined, with IIS.** WebJEA runs as an ASP.NET
(.NET Framework 4.8) application in an IIS application pool.

WebJEA has been tested on Windows Server 2016 and later with PowerShell 5.1. It
should work on Windows Server 2012 R2 with PowerShell 4.0, but that has not been
tested and is not actively supported.

## PowerShell

**PowerShell 5.1** (Windows PowerShell), which is in-box on Windows Server 2016
and later. Scripts you expose through WebJEA run in this engine.

## Service account

The included `Deploy.ps1` assumes a **gMSA** (group Managed Service Account) for
the IIS application pool identity — see
[Creating a gMSA](Installation.md#creating-a-gmsa). A standard AD user account
also works: set `AppPoolUserName` and `AppPoolPassword` in your settings file,
accepting that the password is then stored in that file.

Scripts execute in the context of this account, so grant it only the permissions
your scripts actually need. It does **not** need to be a local administrator on
the web server.

## Certificate (recommended)

`Deploy.ps1` configures HTTPS when you supply `CertThumbprint`, and skips it
(serving plain HTTP) when you leave that setting empty. A certificate is not
technically required, but WebJEA form inputs and script output travel over this
connection — if you pass sensitive data to or from the user, plain HTTP exposes
it to untrusted networks. Use a certificate in any environment that matters.

The certificate must cover the `SiteFQDN` users will browse to and must be
imported into the `LocalMachine\My` store with its private key.

## CPU / RAM

There are no firm CPU or RAM requirements beyond Microsoft's recommendations for
the Windows Server version you are running. Usage drives requirements: WebJEA has
been demonstrated to work on 1 CPU and 1 GB of RAM and runs reliably on 2 CPU
with 4 GB of RAM. What your scripts do will dominate.

## Disk

A few MB beyond your base Windows installation. Again, what you do in PowerShell
will drive the real disk requirements — as will your log retention, since WebJEA
writes both an application log and a usage log (`LogPath` in your settings file).
