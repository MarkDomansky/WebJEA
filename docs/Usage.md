# Usage

## Concepts

* WebJEA is controlled by a JSON configuration file, `config.json`, located in
  your scripts folder (the `ScriptsPath` you chose during
  [installation](Installation.md), e.g. `c:\webjea\config.json`).
* The file has a JSON schema (`schemas/` in this repository). Keep the `$schema`
  line in your config and editors like VS Code will validate and autocomplete it
  as you edit.
* You can also edit the file with the
  [WebJEAConfig](https://www.powershellgallery.com/packages/WebJEAConfig)
  PowerShell module ([repo](https://github.com/markdomansky/WebJEAConfig)), which
  wraps the file in basic Get/Set/New/Remove cmdlets:
  * Open the file with `Open-WebJEAFile`
  * Make changes with `Get`/`Set-Config` and `Get`/`Set`/`New`/`Remove-Command`
  * Save with `Save-WebJEAFile`

## Top-level settings

| Setting | Description |
| ------- | ----------- |
| `Title` | The page title displayed in WebJEA. |
| `BasePath` | Default folder for scripts referenced with a relative path. `Deploy.ps1` sets this to your `ScriptsPath`. |
| `LogParameters` | Whether form inputs are written to the audit log. Default `true`; can be overridden per command. |
| `PermittedGroups` | Default access groups, in `domain\group` format. Members have access to **all** commands. |
| `ShowVerbose` | Show verbose output when a command is run by a member of the global permitted groups. Default `true`. |
| `DefaultCommandId` | `Id` of the command to display by default. |
| `HtmlLanguage` | Language for HTML output, used by screen readers and assistive technologies. Default `en-US`. |
| `SendTelemetry` | Anonymized usage statistics. Set `false` to disable. |
| `Commands` | The list of commands — see below. |

## Commands

Each entry in `Commands` exposes one script as a web form.

| Setting | Description |
| ------- | ----------- |
| `Id` | **Required.** Unique identifier, used in the URL (`?id=<value>`). |
| `DisplayName` | Friendly name presented to users. |
| `Synopsis` | Description shown on the dashboard beneath the command title. May contain HTML, which makes it a good way to point users at the right command. If specified, it overrides the synopsis found in the script. |
| `Script` | The script the form is generated from, and which runs on submit. Parameter sets are **not** supported; most other advanced function features are. |
| `OnloadScript` | Script executed when the command's page loads. It should take no parameters and return quickly. |
| `LogParameters` | Override the config-level `LogParameters` for this command. |
| `PermittedGroups` | Users or groups with access to this command, local or domain, in `domain\user` format. `*` grants access to all users. |
| `RenderMode` | How the command's output is rendered: `Legacy` or `Markdown`. |

## Access control

A user sees only the commands they have access to. Access comes from the
top-level `PermittedGroups` (which applies to every command) plus the command's
own `PermittedGroups`. Group and user names are resolved against Active Directory
by the application pool identity, so that account must be able to read AD.

Remember that scripts execute as the application pool identity, not as the
signed-in user — `PermittedGroups` decides who may *invoke* a script, and the
service account decides what that script *can do*.
