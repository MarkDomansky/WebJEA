# Usage

## Concepts

* WebJEA is controlled by a JSON configuration file, `config.json`, located in your
  scripts folder (the `ScriptsPath` you chose during
  [installation](Installation.md), e.g. `c:\webjea\config.json`).
* The file has a JSON schema — keep the `$schema` line from the starter config and
  editors like VS Code will validate and autocomplete it as you edit.
* There are a handful of top-level settings, including:
  * `Title` — the page title displayed in WebJEA.
  * `DashboardHtml` — HTML shown on the dashboard/landing page.
  * `BasePath` — default folder for scripts referenced with relative paths.
  * `LogParameters` — whether form inputs are written to the audit log (default
    `true`; can be overridden per command).
  * `PermittedGroups` — default access groups that have access to all commands.
  * `ShowVerbose` — show verbose output for members of the global permitted groups.
  * `RenderMode` — default rendering for command output: `Legacy` or `Markdown`.
  * `SendTelemetry` — anonymous usage statistics upload; set `false` to disable.
* `Commands` — each command exposes one script as a web form:
  * `Id` (required) — unique identifier, used in URLs (`?id=<value>`).
  * `DisplayName` — friendly name presented to users.
  * `Synopsis` — description text shown on the dashboard below the command title.
    Can contain HTML; works well to direct users to where they need to go. If
    specified, it overrides what is found in the script.
  * `Script` — the script the form is generated from. It does not support parameter
    sets, but does support most advanced-function features (SYNOPSIS/DESCRIPTION,
    parameter types, `Validate*` attributes).
  * `OnloadScript` — a script that runs when the command's page is loaded, to display
    dynamic data above the form. It should not require parameters and should run
    quickly.
  * `LogParameters` — override the top-level setting for this command.
  * `PermittedGroups` — users or groups that have access to this command
    (`domain\group` format for Windows mode; `*` means any authenticated user). See
    [authentication.md](authentication.md) for Windows vs. Entra ID entry formats.

## Editing the configuration

Edit `config.json` directly (with schema validation in your editor), or use the
[WebJEAConfig](https://www.powershellgallery.com/packages/WebJEAConfig) PowerShell
module ([repo](https://github.com/markdomansky/WebJEAConfig)) with its basic
Get/Set/New/Remove cmdlets:

1. Open the file using `Open-WebJEAFile`
2. Make the changes desired using `Get-`/`Set-Config` and
   `Get-`/`Set-`/`New-`/`Remove-Command`
3. Save the file using `Save-WebJEAFile`

Note: the module was written for the classic configuration format — verify the
result against the schema if you use it with a current release.

## Writing scripts

* Scripts execute under **PowerShell 7** in the context of the WebJEA service
  account — see [powershell7.md](powershell7.md) for compatibility notes.
* The current user is injected as `WEBJEAUSERNAME` (and `$env:WebJEAUserName`), so
  scripts can audit or act on who submitted the form.
* Output streams (output/information/warning/error/verbose/debug) are formatted
  automatically, and `[[a|...]]`, `[[span|...]]`, and `[[img|...]]` markup is
  supported in output — see the [readme](../readme.md) feature list and
  [Tips](Tips.md).
