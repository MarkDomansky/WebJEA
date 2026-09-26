<#
.SYNOPSIS
    Deploys WebJEA to a Hyper-V VM using PowerShell Direct.

.DESCRIPTION
    Entry point that invokes psake tasks defined in integration.psake.ps1.
    Deploys WebJEA to the target VM using a locally built package by default.
    For integration testing, run Build-WebJEA.ps1 first to create the package.

    Alternatively, use -UseGitHubBuild to download the latest release from GitHub instead.

    Use -DoNotRunDeploy to stage the package on the VM (transfer and extract) without
    executing DeployV3.ps1. Useful for validating the staging pipeline or preparing a VM
    for manual deployment.

.PARAMETER ConfigPath
    Path to the configuration JSON file. Defaults to config.json in the script directory.

.PARAMETER UseGitHubBuild
    Download and deploy from GitHub instead of using the local build.
    By default, the script uses the local build created by Build-WebJEA.ps1.

.EXAMPLE
    .\Build-WebJEA.ps1
    .\Deploy-WebJEA.ps1

.EXAMPLE
    .\Deploy-WebJEA.ps1 -UseGitHubBuild

.EXAMPLE
    .\Deploy-WebJEA.ps1 -ConfigPath .\custom-config.json

.PARAMETER DoNotRunDeploy
    Stage the package on the VM (transfer and extract) without executing DeployV3.ps1.

.PARAMETER DeployEdition
    Which PowerShell edition runs Deploy.ps1 on the VM. 'WindowsPowerShell' (default)
    runs it in the PowerShell Direct session, which is Windows PowerShell 5.1.
    'PowerShell7' shells out to pwsh.exe on the VM instead, which must be installed
    there. Deploy.ps1 supports both; run it each way to cover both paths.

.PARAMETER ResetVM
    Revert the Web Server VM to the baseline snapshot before deploying.
    When specified, the VM is stopped, reverted to the snapshot defined in config.json (HyperV.SnapshotName),
    and started fresh before the deployment begins.

.EXAMPLE
    .\Deploy-WebJEA.ps1 -DoNotRunDeploy

.EXAMPLE
    .\TestDeploy.ps1 -ResetVM

.EXAMPLE
    .\TestDeploy.ps1 -ResetVM -UseGitHubBuild

.EXAMPLE
    # Cover the PowerShell 7 path (pwsh.exe must be installed on the VM)
    .\TestDeploy.ps1 -ResetVM -DeployEdition PowerShell7

.NOTES
    Requires Hyper-V PowerShell module and psake module.
    Must be run on the Hyper-V host with appropriate permissions.
    The target VM must support PowerShell Direct (Windows 10/Server 2016+).
#>
#Requires -Version 5.1
#Requires -Modules "Hyper-V", "psake"

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ConfigPath = ("$PSScriptRoot\config.json"),

    [Parameter()]
    [switch]$UseGitHubBuild,

    [Parameter()]
    [switch]$QuickBuild,

    [Parameter()]
    [switch]$DoNotRunDeploy,

    [Parameter()]
    [ValidateSet('WindowsPowerShell', 'PowerShell7')]
    [string]$DeployEdition = 'WindowsPowerShell',

    [Parameter()]
    [switch]$ResetVM
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name psake)) {
    Write-Host 'psake module not found. Installing from PSGallery...' -ForegroundColor Yellow
    Install-Module -Name psake -Scope CurrentUser -Force
}
Import-Module psake -ErrorAction Stop

$taskList = if ($ResetVM) { @('RevertVMs', 'Deploy') } else { @('Deploy') }

$psakeParams = @{
    buildFile  = "$PSScriptRoot\integration.psake.ps1"
    taskList   = $taskList
    properties = @{
        configPath     = $ConfigPath
        helpersPath    = Join-Path $PSScriptRoot 'Helpers'
        useGitHubBuild = [bool]$UseGitHubBuild
        quickBuild     = [bool]$QuickBuild
        doNotRunDeploy = [bool]$DoNotRunDeploy
        deployEdition  = $DeployEdition
        resetVM        = [bool]$ResetVM
    }
    nologo     = $true
}

Invoke-psake @psakeParams

if (-not $psake.build_success) {
    exit 1
}

exit 0
