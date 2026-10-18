<#
.SYNOPSIS
    Runs integration tests against the WebJEA website.

.DESCRIPTION
    Entry point that invokes psake tasks defined in integration.psake.ps1.
    Uses Pester to execute integration tests against the WebJEA website.
    Tests include authentication, page accessibility, and basic functionality validation.

.PARAMETER ConfigPath
    Path to the configuration JSON file. Defaults to config.json in the script directory.

.PARAMETER Tags
    Specific test tags to run. If not specified, uses tags from configuration.

.PARAMETER ExcludeTags
    Test tags to exclude from the run.

.PARAMETER OutputPath
    Path for test result output files.

.PARAMETER Auth
    Reconfigure the deployed site's authentication before testing: 'AD' (Windows/Kerberos),
    'Entra' (OIDC), or 'Dev' (DevAutoLogin). Merges appsettings.Production.json (next to
    config.json; copy it from appsettings.Production.template.json) into the site's
    appsettings.Production.json on the web VM and restarts the service. 'Dev' also sets
    ASPNETCORE_ENVIRONMENT=Development on the service; 'AD' and 'Entra' clear it.
    Omit to test the site as currently configured.

.EXAMPLE
    .\Invoke-IntegrationTests.ps1

.EXAMPLE
    .\Invoke-IntegrationTests.ps1 -Tags 'Smoke', 'Authentication'

.EXAMPLE
    .\Invoke-IntegrationTests.ps1 -ConfigPath .\custom-config.json -OutputPath .\Results

.EXAMPLE
    .\Invoke-IntegrationTests.ps1 -Auth Entra

.NOTES
    Requires Pester v5.0 or later and psake module.
    Tests use Windows authentication against the target website.
#>
#Requires -Version 5.1
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'config.json'),

    [Parameter()]
    [string[]]$Tags,

    [Parameter()]
    [string[]]$ExcludeTags,

    [Parameter()]
    [string]$OutputPath,

    [Parameter()]
    [ValidateSet('AD', 'Entra', 'Dev')]
    [string]$Auth
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name psake)) {
    Write-Host 'psake module not found. Installing from PSGallery...' -ForegroundColor Yellow
    Install-Module -Name psake -Scope CurrentUser -Force
}
Import-Module psake -ErrorAction Stop

$psakeProperties = @{
    configPath  = $ConfigPath
    helpersPath = Join-Path $PSScriptRoot 'Helpers'
}

if ($Tags)        { $psakeProperties.tags        = $Tags }
if ($ExcludeTags) { $psakeProperties.excludeTags  = $ExcludeTags }
if ($OutputPath)  { $psakeProperties.outputPath   = $OutputPath }
if ($Auth)        { $psakeProperties.auth         = $Auth }

$taskList = if ($Auth) { @('ConfigureAuth', 'IntegrationTest') } else { @('IntegrationTest') }

$psakeParams = @{
    buildFile  = Join-Path $PSScriptRoot 'integration.psake.ps1'
    taskList   = $taskList
    properties = $psakeProperties
    nologo     = $true
}

Invoke-psake @psakeParams

if (-not $psake.build_success) {
    exit 1
}

exit 0
