# PowerShell 7 script compatibility

WebJEA now hosts **PowerShell 7.6** in-process (via `Microsoft.PowerShell.SDK`) instead
of Windows PowerShell 5.1. Most scripts run unchanged, but review the differences below
before upgrading a production instance. The simplest pre-flight check: run each
registered script (and onload script) manually under `pwsh` on the WebJEA server.

## What changes

- `$PSVersionTable.PSEdition` is `Core` and `PSVersion` is 7.x. Scripts that branch on
  edition/version behave accordingly.
- **`Get-WmiObject` and the `*-Wmi*` cmdlets are gone.** Use the CIM cmdlets
  (`Get-CimInstance`, `Invoke-CimMethod`, ...).
- **`Add-PSSnapin` is gone.** Snap-in-based tooling (some legacy Exchange/third-party
  snap-ins) must move to modules or remoting.
- **Windows-PowerShell-only modules** may not load natively. PowerShell 7 can proxy many
  of them via the compatibility layer (`Import-Module <name> -UseWindowsPowerShell`),
  but proxied objects are deserialized — method calls on returned objects will not work.
  The ActiveDirectory RSAT module, Exchange Online v3, Az, and most current first-party
  modules support PowerShell 7 natively.
- **Default file encoding is UTF-8 (no BOM)** for `Out-File`/redirection instead of
  UTF-16/ANSI. Pass `-Encoding` explicitly where downstream consumers care.
- Console-less `Out-String` formatting width can differ from PS 5.1; explicit
  `Format-Table -AutoSize | Out-String -Width 200` gives deterministic output.
- Module autoloading pulls from the PowerShell 7 module paths
  (`$env:ProgramFiles\PowerShell\Modules`, ...). Modules installed only into the
  Windows PowerShell module path for the WebJEA service account may need reinstalling
  with `pwsh -Command Install-Module ...`.

## What stays the same

- Advanced-function metadata parsing (SYNOPSIS/DESCRIPTION, parameter types, Validate*
  attributes) is unchanged — the same scripts produce the same forms.
- Scripts still execute under the WebJEA service identity (the Windows service account,
  or the container's configured identity), one runspace per request, with
  `WEBJEAUSERNAME`/`WEBJEAHOSTNAME` injected and `$env:WebJEAUserName` set.
- Output streams (output/info/warning/error/verbose/debug), the `WEBJEA:` log-line
  directive, and the `[[a|...]]`/`[[span|...]]`/`[[img|...]]` markup all behave as
  before.

## Linux note

When hosting WebJEA on Linux (Entra authentication mode), scripts run under `pwsh` on
Linux: no Windows-specific modules (ActiveDirectory, IIS, ScheduledTasks, ...), and
paths/permissions follow Linux conventions. See [authentication.md](authentication.md).
