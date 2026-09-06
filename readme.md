# WebJEA: PowerShell driven Web Forms for Secure Self-Service

WebJEA allows you to dynamically build web forms for any PowerShell script.  WebJEA automatically parses the script at page load for description, parameters and validation, then dynamically builds a form to take input and display formatted output.  You define access groups via AD and the scripts run within the WebJEA service account's context.

_WebJEA does not require JEA endpoints but can work with them.  With WebJEA, any PS script you write can be exposed to a controlled set of users via a web interface._


## Goals

The main goals for WebJEA:

* Reduce delegation of privileged access to users
* Quickly automate on-demand tasks and grant access to less-privileged users
* Leverage your existing knowledge in PowerShell to build web forms and automate on-demand processes
* Encourage proper script creation by parsing and honoring advanced function parameters and comments

## Features

* Control access via Active Directory groups and users, or Entra ID groups, users, and app roles (user only sees scripts they have access to) — see [docs/authentication.md](docs/authentication.md)
* Mobile support using a responsive UI
* Parses PowerShell advanced functions formatting for SYNOPSIS, DESCRIPTION, parameter names, variable types, and validation requirements
* Supports pre-populating forms from ticketing systems or other sources via
  query-string parameters on `/command.html?cmdid=...` (GET only — keep values
  short and non-sensitive: they appear in logs and browser history, and very long
  query strings can hit server or proxy limits). POST to any page URL returns 405;
  the API is the only POST surface.
* Onload script allows you to run a powershell script on page load to display dynamic data before the form
* Scripts run in the context of the WebJEA Windows service account (or the container's configured identity) allowing for granular permissions.
* Supports content formatting output including:
  * Automatic formatting for Write-Error, Write-Warning, Write-Verbose, and Write-Debug
    * Use \$\*ActionPreference to control display of each stream
  * Links: \[a|url|display\]
  * Images: \[img|cssclass|url\]
  * Spans: \[span|cssclasses|display\]
  * Nesting is supported (e.g. a link can contain an image)
  * Add and modify css tags in psoutput.css to alter output.
* Anonymous usage data is uploaded to AWS for statistical reporting.  This can be disabled.
* GPL v3 licensed
* NLOG is used to output debug as well as usage data. <br>_A dedicated log file for usage is included in the NLOG configuration and will output usage including what scripts are run, by who, and for how long._

## Requirements

* WebJEA runs on **ASP.NET Core (.NET 10)** and hosts **PowerShell 7** in-process. Scripts execute under PowerShell 7 — see [docs/powershell7.md](docs/powershell7.md) for the (small) compatibility differences from Windows PowerShell 5.1.
* Windows Server for the classic domain-joined deployment with Integrated Windows Authentication; a domain-joined server is required for AD group authorization. WebJEA installs as a self-hosted **Windows service** on Kestrel — no IIS role and no .NET runtime prerequisite, since the release is a self-contained package.
* Alternatively, hosting on Linux (or Windows without AD) is supported using **Entra ID** authentication — see [docs/authentication.md](docs/authentication.md).
* Docker images for Linux and Windows containers can be built from the included configurations — see [docs/docker.md](docs/docker.md).
* CPU/RAM Requirements will depend significantly on your usage. <br>_Expect roughly the cost of spinning up a PowerShell 7 runspace per execution plus typical ASP.NET Core consumption.  Your usage will vary greatly depending on what your script does._

### Recommended

The following are recommendations, and are pre-configured in the provided DSC configuration, but are not strictly required.

* SSL Certificate
* A gMSA (group Managed Service Account) running the WebJEA Windows service (Windows 2008 R2 Forest/Domain functional level).  Read more about MSAs [here](https://technet.microsoft.com/en-us/library/dd560633(v=ws.10).aspx).<br>_You can use a standard AD user account with a password instead — `Deploy.ps1` supports both via the `ServiceUserName`/`ServicePassword` settings, no changes needed._
* Active Directory.  AD is not strictly required and limited testing for local users and groups confirms WebJEA works with local users, but they have not been thoroughly tested and are not recommended.

The DSC configuration included allows quick deployment and should be suitable in most environments.

## Limitations

There are some limitations with WebJEA.  All of these are considered areas for future improvement so please give your feedback via [Issues](https://github.com/markdomansky/WebJEA/issues).

* Scripts run with limited feedback.  There is a "spinner", but nothing gets fed back to the client until the script is finished.
* Write-Progress is ignored for the same reason.
* PSCredential is not a supported input.
* All Parameters are passed to the script as strings.
* All Output is treated as a string.<br> _All output is piped to Out-String to generate usable output from any return data.  To control your output more granularly, use format-table/list, selects, etc before Out-String receives the data._
* Supported authentication methods are Integrated Windows Authentication and Entra ID (see [docs/authentication.md](docs/authentication.md)).

## Installation

The included `Deploy.ps1` installs and configures WebJEA as a self-hosted **Windows service** on Kestrel — no IIS, no prerequisites to install (the release is self-contained).  Running multiple instances (formerly sub-application installs under one IIS site) is now done with one container per instance — see [docs/docker.md](docs/docker.md).  See [docs/deployment-migration.md](docs/deployment-migration.md) for what changed from the WebForms/IIS releases, including how to migrate an existing IIS install, and for manual Linux (Debian/RHEL) hosting instructions.  Check the [Documentation](docs/README.md) for more information.

**Breaking change:** `WebJEA:HttpsPort` now also enables the HTTPS listener; it is no longer a redirect-target-only setting — see [docs/deployment-migration.md](docs/deployment-migration.md).

Installation Steps:
1. Build a server, get a certificate, create a managed service account (gMSA).
2. Go to [Releases](https://github.com/markdomansky/WebJEA/releases), download and extract the latest release.
3. Copy `settings.template.json` to e.g. `settings.json` and fill in the machine/site name, certificate thumbprint, service account, ports, and deployment folders — see [docs/Installation.md](docs/Installation.md).
4. From an elevated **PowerShell 7** (`pwsh`) prompt — not Windows PowerShell 5.1 — run `.\Deploy.ps1 -SettingsFile .\settings.json`. This installs the required PowerShell modules, copies the release, and creates and starts the WebJEA Windows service.
5. Reboot should not be needed, but is recommended following first deployment.

A demo script is included to confirm operation. Use the WebJEAConfig module to add additional scripts below.

## Adding Scripts to WebJEA

WebJEAConfig is a PowerShell Gallery Package to modify your WebJEA Configuration.

#### Installation

```powershell
Install-Module WebJEAConfig
```

#### Adding a script

```powershell
#Config.Json location and other inputs will depend on your specific configuration.
Import-Module WebJEAConfig
Open-WebJEAFile -Path "c:\webjea\config.json" 
New-WebJEACommand -CommandId 'id' -DisplayName 'DisplayName' -Script 'script.ps1' -PermittedGroups @('*')
Save-WebJEAFile
```

## Security Considerations

* Managed Service Accounts let Active Directory automatically manage the password, never exposing it to you or anyone else, just like a computer account does.  This means any permissions you grant the MSA can only be executed on that server, by the WebJEA Windows service.
* Don't configure the MSA as a local administrator on the server.  It is not necessary to run.  However, it does need to be a local user and will automatically grant itself this permission during the DSC execution.
* Ideally, you should grant the MSA account only the minimum, precise permissions needed to perform the tasks in your scripts.  <br>_For example, if you want to create a Help Desk unlock tool, you don't grant the MSA domain admin or even account operator.  Create a custom permission in AD that allows the MSA to unlock user accounts._

## In-place Upgrades

In-place upgrades remain straightforward: replace the `.\site` files in place, but keep your local configuration (`appsettings.Production.json` or environment variables, and `nlog.config`).  Upgrading from a WebForms (pre-.NET 10) release is **not** an in-place upgrade — see [docs/deployment-migration.md](docs/deployment-migration.md).

## License

Copyright, 2018, Mark Domansky.  All rights not granted explicitly are reserved.
This code is released under the GPL v3 license.

See the [License](LICENSE) and [Attributions](LICENSE-attributions) for details.

