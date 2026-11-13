# Usage

## Concepts

* WebJEA is controlled by a JSON configuration file, `webjea.json`, located in your scripts folder (the `ScriptsPath` you chose during [installation](Installation.md), e.g. `c:\webjea\webjea.json`).
* The file has a JSON schema — keep the `$schema` line from the starter config and editors like VS Code will validate and autocomplete it as you edit.
* There are a handful of top-level settings, including:
  * `Title` — the page title displayed in WebJEA.
  * `DashboardHtml` — HTML shown on the dashboard/landing page.
  * `NoAccessHelpHtml` — HTML shown after the "Not available" message (see [What users see when something goes wrong](#what-users-see-when-something-goes-wrong)), e.g. a link to your access-request form. Defaults to "Contact your WebJEA administrator if you need access."
  * `BasePath` — default folder for scripts referenced with relative paths.
  * `LogParameters` — whether form inputs are written to the audit log (default `true`; can be overridden per command).
  * `PermittedGroups` — default access groups that have access to all commands.
  * `ShowVerbose` — whether members of the global `PermittedGroups` can see verbose output (default `true`). Applies to global users only; other users never see verbose output. There is no per-command override.
  * `RenderMode` — default rendering for command output: `Legacy` (default), `Markdown` or `Link`; can be overridden per command. `Markdown` and `Link` require v2100 or later.
  * `SendTelemetry` — anonymous usage statistics upload; set `false` to disable.
* `Commands` — each command exposes one script as a web form:
  * `Id` (required) — unique identifier, used in URLs (`?id=<value>`).
  * `DisplayName` — friendly name presented to users.
  * `Synopsis` — description text shown on the dashboard below the command title. Can contain HTML; works well to direct users to where they need to go. If specified, it overrides what is found in the script.
  * `Script` — the script the form is generated from. It does not support parameter sets, but does support most advanced-function features (SYNOPSIS/DESCRIPTION, parameter types, `Validate*` attributes).
  * `OnloadScript` — a script that runs when the command's page is loaded, to display dynamic data above the form. It should not require parameters and should run quickly.
  * `LogParameters` — override the top-level setting for this command.
  * `PermittedGroups` — users or groups that have access to this command (`domain\group` format for Windows mode; `*` means any authenticated user). See [authentication.md](authentication.md) for Windows vs. Entra ID entry formats.

## Editing the configuration

Edit `webjea.json` directly in an editor that understands JSON schemas, such as VS Code. Keep this line at the top of the file:

```json
"$schema": "https://www.schemastore.org/webjea.json",
```

The editor then autocompletes property names, shows each setting's description, and flags wrong types and missing required values (`Title`, `Commands`, and each command's `Id`) before WebJEA loads the file. Changes take effect on the next page load.

The WebJEAConfig PowerShell module is retired and does not support settings added since the classic release (such as `DashboardHtml`, `NoAccessHelpHtml` and `RenderMode`). Use the schema instead.

## What users see when something goes wrong

WebJEA shows users a short message and writes the details to `webjea.log`. Users see the same "Not available" message whether no commands are configured, they aren't permitted on any, or they opened a command that doesn't exist or that they aren't permitted on. This way the page never reveals which command Ids exist. The log says which case it was:

| Users see | Log entry (Warn) | Cause |
|---|---|---|
| **Not available.** + `NoAccessHelpHtml` | `API: Menu empty for <user>: no commands are configured` | `"Commands": []` |
| | `API: Menu empty for <user>: permitted on 0 of N commands` | The user is in none of the commands' `PermittedGroups` |
| | `API: User <user> requested cmdid <id>: not found in config` | A link or bookmark to a command Id that doesn't exist |
| | `API: User <user> denied cmdid <id>: not in any permitted group` | The command exists, but the user isn't permitted on it |
| **Something went wrong.** + a reference ID | An Error entry with the same request ID | For example, `webjea.json` doesn't parse or is missing `Commands` |
| **Can't reach WebJEA.** | None | The server is down or unreachable |
| **Please sign in again.** | None, or in Entra mode `Entra: signed out <user> before execute: sign-in no longer valid (...)` | Sign-in expired or, in Entra mode, was revoked ([Near-real-time revocation](entra.md#near-real-time-revocation)); reloading the page signs the user in again |
| **Your access has changed.** (Entra mode) | `API: User <user> access to cmdid <id> removed since sign-in (current Entra groups)` (Info) | The user was removed from the command's groups after signing in; reloading the page updates the menu |
| **Couldn't verify your sign-in.** + a reference ID (Entra mode) | `Entra: couldn't revalidate <user> (...); execute refused.` | Microsoft Graph couldn't be reached or denied access, so the script didn't run ([Near-real-time revocation](entra.md#near-real-time-revocation)) |

Every `webjea.log` line includes the request ID, so you can search the log for the reference ID a user reports.

## Writing scripts

* Scripts execute under **PowerShell 7** in the context of the WebJEA service account — see [powershell7.md](powershell7.md) for compatibility notes.
* The current user is injected as `WEBJEAUSERNAME` (and `$env:WebJEAUserName`), so scripts can audit or act on who submitted the form.
* Output streams (output/information/warning/error/verbose/debug) are formatted automatically, and `[[a|...]]`, `[[span|...]]`, and `[[img|...]]` markup is supported in output — see the [readme](../readme.md) feature list and [Tips](Tips.md).
