# WebJEA: PowerShell driven Web Forms for Secure Self-Service

WebJEA allows you to dynamically build web forms for any PowerShell script.  WebJEA automatically parses the script at page load for description, parameters and validation, then dynamically builds a form to take input and display formatted output.  You define access groups via AD and the scripts run within the AppPool user context.

_WebJEA does not require JEA endpoints but can work with them.  With WebJEA, any PS script you write can be exposed to a controlled set of users via a web interface._


## Goals

The main goals for WebJEA:

* Reduce delegation of privileged access to users
* Quickly automate on-demand tasks and grant access to less-privileged users
* Leverage your existing knowledge in PowerShell to build web forms and automate on-demand processes
* Encourage proper script creation by parsing and honoring advanced function parameters and comments

## Features

* Control access via Active Directory groups and users (user only sees scripts they have access to)
* Mobile support using a responsive UI
* Parses PowerShell advanced functions formatting for SYNOPSIS, DESCRIPTION, parameter names, variable types, and validation requirements
* Supports GET/POST inputs allowing pre-populating forms from ticketing systems or other sources
* Onload script allows you to run a powershell script on page load to display dynamic data before the form
* Scripts run in the context of the App Pool allowing for granular permissions.
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

* Domain Joined server running Windows 2016 Core/Full with PowerShell 5.1 <br>_(Windows 2012 R2 or PowerShell 4.0 (perhaps with a recompile) should work, but it hasn't been tested.)_
* CPU/RAM Requirements will depend significantly on your usage. <br>_My testing shows about 40 MB just to spin up a PowerShell thread plus typical ASP.NET consumption.  Your usage will vary greatly depending on what your script does.  I'm successfully running WebJEA in production on Windows 2016  core, 2-vCPU, 4GB RAM with light usage._

### Recommended

The following are recommendations, and are pre-configured in the provided DSC configuration, but are not strictly required.

* SSL Certificate
* Managed Service Account on the IIS App Pool (Windows 2008 R2 Forest/Domain functional level).  Read more about MSAs [here](https://technet.microsoft.com/en-us/library/dd560633(v=ws.10).aspx).<br>_You can use a standard AD user account, but you'll need to modify the DSC accordingly. Standalone MSAs introduced in 2008 AD work as well._
* Active Directory.  AD is not strictly required and limited testing for local users and groups confirms WebJEA works with local users, but they have not been thoroughly tested and are not recommended.

The DSC configuration included allows quick deployment and should be suitable in most environments.

## Limitations

There are some limitations with WebJEA.  All of these are considered areas for future improvement so please give your feedback via [Issues](https://github.com/markdomansky/WebJEA/issues).

* Scripts run with limited feedback.  There is a "spinner", but nothing gets fed back to the client until the script is finished.
* Write-Progress is ignored for the same reason.
* PSCredential is not a supported input.
* All Parameters are passed to the script as strings.
* All Output is treated as a string.<br> _All output is piped to Out-String to generate usable output from any return data.  To control your output more granularly, use format-table/list, selects, etc before Out-String receives the data._
* Integrated Authentication.  No authentication method other than WIA has been tested.

## Installation

`Deploy.ps1` is provided to get you going quickly.  Check the [Documentation](docs/README.md) for more information, in particular the [Installation guide](docs/Installation.md) and [System Requirements](docs/System-Requirements.md).

Installation Steps:
1. Build a server, get a certificate, create a managed service account.
2. Go to [Releases](https://github.com/markdomansky/WebJEA/releases), download and extract the latest release.
3. Copy `settings.template.jsonc` and fill in the machine name, certificate thumbprint, MSA username, deployment folders, etc.  Every setting is described in the [settings reference](docs/Installation.md#settings-reference).
4. From an elevated prompt, run `.\Deploy.ps1 -SettingsFile .\settings.jsonc`.  This installs the necessary DSC modules and Windows features, then deploys the site.  Add `-TestOnly` first to see what would change.
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

* Managed Service Accounts let Active Directory automatically manage the password, never exposing it to you or anyone else, just like a computer account does.  This means any permissions you grant the MSA can only be executed on that server, by the AppPool.
* Don't configure the MSA as a local administrator on the server.  It is not necessary to run.  However, it does need to be a local user and will automatically grant itself this permission during the DSC execution.
* Ideally, you should grant the MSA account only the minimum, precise permissions needed to perform the tasks in your scripts.  <br>_For example, if you want to create a Help Desk unlock tool, you don't grant the MSA domain admin or even account operator.  Create a custom permission in AD that allows the MSA to unlock user accounts._

## In-place Upgrades

In-place upgrades are very straightforward.  You can use DSC, or just drop the .\Site files in place.  Be careful not to overwrite the existing config files (nlog.config and web.config).  It should take effect immediately without an iisreset.

## License

Copyright, 2018, Mark Domansky.  All rights not granted explicitly are reserved.
This code is released under the GPL v3 license.

See the [License](LICENSE) and [Attributions](LICENSE-attributions) for details.

